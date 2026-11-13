using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace Degausser;

/// <summary>
/// Converts Band Brothers score semantics into a standard DryWetMIDI model.
/// Band Brothers itself uses 12 score frames per quarter note. MIDI output is
/// written at 480 PPQ (40 MIDI ticks per BBP frame), preserving the game's
/// score-frame timing exactly while remaining convenient for standard MIDI tools.
/// </summary>
public static class BbpToMidiConverter
{
    public const short TicksPerQuarterNote = 480;
    public const int TicksPerFrame = TicksPerQuarterNote / 12;

    private const int SourceChannelCount = 10;
    private const byte GeneralMidiDrumChannel = 9;

    // Native P note-strength tables selected by ChannelInfo +0x06.
    // Strength code 2 is the normal/reference attack.
    private static readonly byte[][] NoteStrengthCurves =
    [
        [70, 85, 100, 115],
        [50, 75, 100, 125],
        [80, 90, 100, 110]
    ];

    public static BbpMidiDocument Convert(BBPRecord record, BbpMidiConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        options ??= new BbpMidiConversionOptions();
        BbpMidiProfile profile = options.Profile ?? BbpMidiProfile.Mosgis;

        int lengthFrames = record.LengthFrames;
        var channelInfos = new BbpMidiChannelInfo[SourceChannelCount];
        var notesByChannel = new List<NoteSpec>[SourceChannelCount];

        int drumPort = 0;
        for (int source = 0; source < SourceChannelCount; source++)
        {
            ChannelInfo info = source < record.gak.channelInfo.Length
                ? record.gak.channelInfo[source]
                : default;
            BbpMidiProfile.Instrument mapped = profile.GetInstrument(info.instrument);
            BbpInstrumentInfo instrument = new(info.instrument, mapped.Name, mapped.MidiProgram,
                mapped.DrumNotes, mapped.SfBank, mapped.SfProgram);
            bool isDrum = info.playType == PlayType.Drum || instrument.IsDrum;
            bool isUnused = info.instrument == 0;
            byte playbackChannel = isDrum ? checked((byte)(drumPort++ * 16 + GeneralMidiDrumChannel)) : SourceToNonPercussionChannel(source);
            byte canonicalChannel = isDrum ? GeneralMidiDrumChannel : playbackChannel;
            string clone = info.cloneID > 0 ? $" {info.cloneID}" : string.Empty;
            string displayName = isUnused
                ? "Unused"
                : instrument.Name + clone + $" ({info.playType})";

            channelInfos[source] = new BbpMidiChannelInfo(
                source, playbackChannel, canonicalChannel, info.instrument, displayName, info.playType, isDrum, isUnused,
                instrument.SoundFontBank, instrument.SoundFontProgram);
            notesByChannel[source] = BuildNotes(record, source, instrument, lengthFrames);
            if (mapped.Transpose != 0)
                notesByChannel[source] = notesByChannel[source]
                    .Select(n => n with { Note = (byte)Math.Clamp(n.Note + mapped.Transpose, 0, 127) })
                    .ToList();
        }

        MidiFile canonical = BuildFile(record, profile, channelInfos, notesByChannel, lengthFrames);
        // Preview plays this same MIDI (the desktop player runs one synthesizer
        // per port), so it cannot drift from the export.
        MidiFile playback = canonical;
        return new BbpMidiDocument(
            canonical, playback, channelInfos, lengthFrames, TicksPerFrame,
            record.gak.masterGain);
    }

    private static List<NoteSpec> BuildNotes(BBPRecord record, int source, BbpInstrumentInfo instrument, int lengthFrames)
    {
        var result = new List<NoteSpec>();
        if (source >= record.gak.channelInfo.Length || source >= record.gak.channelNotes.Length)
            return result;

        ChannelInfo info = record.gak.channelInfo[source];
        byte[] notes = record.gak.channelNotes[source].notes ?? [];
        PackedNoteStrengths strengths = source < record.gak.noteStrengths.Length
            ? record.gak.noteStrengths[source]
            : default;

        switch (info.playType)
        {
            case PlayType.Standard:
                BuildStandardNotes(info, strengths, notes, lengthFrames, result);
                break;

            case PlayType.Drum:
                BuildDrumNotes(info, strengths, notes, instrument.DrumNotes, lengthFrames, result);
                break;

            case PlayType.Guitar:
                BuildGuitarNotes(record.gak, info, strengths, notes, lengthFrames, result);
                break;

            case PlayType.Piano:
                BuildPianoNotes(record.gak, info, strengths, notes, lengthFrames, result);
                break;
        }

        return result;
    }

    private static void BuildStandardNotes(
        ChannelInfo info,
        PackedNoteStrengths strengths,
        byte[] notes,
        int lengthFrames,
        List<NoteSpec> output)
    {
        int? activeIndex = null;
        foreach (ScoreSlot slot in EnumerateScoreSlots(notes, lengthFrames, 0xFF))
        {
            byte note = slot.Value;
            if (note >= 128) continue;

            long tick = FrameToTick(slot.Frame);
            if (activeIndex is int index)
            {
                CloseNote(output, index, tick);
                activeIndex = null;
            }

            if (note > 0)
            {
                byte velocity = AttackVelocity(
                    info, strengths, slot.SourceIndex, StrengthLane.Melodic);
                output.Add(new NoteSpec(tick, Math.Max(1, FrameToTick(lengthFrames) - tick), note, velocity));
                activeIndex = output.Count - 1;
            }
        }

        if (activeIndex is int last)
            CloseNote(output, last, FrameToTick(lengthFrames));
    }

    private static void BuildPianoNotes(
        Gak gak,
        ChannelInfo info,
        PackedNoteStrengths strengths,
        byte[] notes,
        int lengthFrames,
        List<NoteSpec> output)
    {
        var active = new List<int>(4);
        foreach (ScoreSlot slot in EnumerateScoreSlots(notes, lengthFrames, 0xFF))
        {
            byte scoreCode = slot.Value;
            if (scoreCode >= 128) continue;

            long tick = FrameToTick(slot.Frame);
            CloseNotes(output, active, tick);
            if (scoreCode == 0) continue;

            // Native P Piano score bytes are 1-based indices into pianoPair[120].
            // The selected pair is either a native generated chord descriptor or
            // an original/custom selector into pianoOrig[64].
            int pairSlot = scoreCode - 1;
            if ((uint)pairSlot >= (uint)gak.pianoPair.Length)
                continue;

            IndexRootPair pair = gak.pianoPair[pairSlot];
            if (!BbpPianoChordCatalog.TryResolve(pair, gak.pianoOrig, out byte[] chord))
                continue;

            byte velocity = AttackVelocity(
                info, strengths, slot.SourceIndex, StrengthLane.Melodic);

            for (int i = 0; i < chord.Length; i++)
            {
                byte midiNote = chord[i];
                if (midiNote is 0 or >= 128) continue;

                // Native P preserves all four custom-piano voices exactly, even
                // when two stored voices use the same pitch. Do not deduplicate.
                output.Add(new NoteSpec(tick, Math.Max(1, FrameToTick(lengthFrames) - tick), midiNote, velocity));
                active.Add(output.Count - 1);
            }
        }

        CloseNotes(output, active, FrameToTick(lengthFrames));
    }

    private static void BuildGuitarNotes(
        Gak gak,
        ChannelInfo info,
        PackedNoteStrengths strengths,
        byte[] notes,
        int lengthFrames,
        List<NoteSpec> output)
    {
        var palettes = gak.guitarTimer
            .TakeWhile(static p => p.time >= 0)
            .ToArray();
        if (palettes.Length == 0) return;

        var active = new List<int>(6);
        foreach (ScoreSlot slot in EnumerateScoreSlots(notes, lengthFrames, 0xFF))
        {
            byte code = slot.Value;
            long attackTick = FrameToTick(slot.Frame);
            ushort metadata = (uint)slot.SourceIndex < (uint)gak.guitarNoteMetadata.Length
                ? gak.guitarNoteMetadata[slot.SourceIndex]
                : (ushort)0;
            int button = BbpChordCatalog.ButtonForAttackCode(code);

            // Native P can retrigger a held/high-bit Guitar score byte. 0x8000
            // in the parallel metadata is the authoritative fresh-strum marker.
            // Low-byte attacks remain as a compatibility fallback for synthetic
            // and old converted files whose derived metadata is absent.
            bool metadataAttack = (metadata & 0x8000) != 0;
            bool legacyLowAttack = code > 0 && code < 0x80 && button >= 0;
            bool isAttack = metadataAttack || legacyLowAttack;

            if (!isAttack)
            {
                if (code == 0)
                    CloseNotes(output, active, attackTick);
                continue;
            }

            if (button < 0)
            {
                CloseNotes(output, active, attackTick);
                continue;
            }

            GuitarStuff palette = PaletteAt(palettes, slot.Frame);
            int paletteSlot = BbpChordCatalog.GuitarPlayableSlots[button];
            if (palette.pair is null || (uint)paletteSlot >= (uint)palette.pair.Length)
            {
                CloseNotes(output, active, attackTick);
                continue;
            }

            IndexRootPair pair = palette.pair[paletteSlot];
            if (!BbpChordCatalog.TryResolveGuitarVoicing(pair, gak.guitarOrig, out uint encoded, out byte[] chord))
            {
                CloseNotes(output, active, attackTick);
                continue;
            }

            int suppressedMask = metadata & 0x3F;
            // Real-3DS ground truth: metadata bit 0x4000 means a down arrow;
            // clear means up. If metadata is absent, fall back to the score code.
            bool down = metadataAttack
                ? (metadata & 0x4000) != 0
                : !BbpChordCatalog.IsUpStrum(code);
            byte velocity = AttackVelocity(
                info, strengths, slot.SourceIndex, StrengthLane.Melodic);

            // Every arrow is a discrete envelope retrigger. End the previous
            // ringing chord exactly at this tick; Note Off events are ordered
            // before Note On events at equal timestamps by BuildChannelTrack.
            CloseNotes(output, active, attackTick);

            var ringingPitches = new HashSet<byte>();
            for (int stringIndex = 0; stringIndex < 6; stringIndex++)
            {
                int packedFret = (int)((encoded >> (5 * stringIndex)) & 31);
                bool eventSuppressed = (suppressedMask & (1 << stringIndex)) != 0;
                if (packedFret < 16 && !eventSuppressed)
                {
                    byte note = chord[stringIndex];
                    if (note is > 0 and < 128) ringingPitches.Add(note);
                }
            }

            if (down)
            {
                // Tuning/packed-fret arrays are high E -> low E; a downstroke
                // reaches low E first. Strings still share one authored onset.
                for (int stringIndex = 5; stringIndex >= 0; stringIndex--)
                    AddGuitarString(output, active, chord, encoded, suppressedMask, stringIndex, attackTick, velocity, lengthFrames, ringingPitches);
            }
            else
            {
                for (int stringIndex = 0; stringIndex < 6; stringIndex++)
                    AddGuitarString(output, active, chord, encoded, suppressedMask, stringIndex, attackTick, velocity, lengthFrames, ringingPitches);
            }
        }

        CloseNotes(output, active, FrameToTick(lengthFrames));
    }

    private static void AddGuitarString(
        List<NoteSpec> output,
        List<int> active,
        IReadOnlyList<byte> chord,
        uint encoded,
        int suppressedMask,
        int stringIndex,
        long attackTick,
        byte velocity,
        int lengthFrames,
        IReadOnlySet<byte> ringingPitches)
    {
        // Event-mask bits mean the pick/strum does not hit this physical string.
        // This is distinct from a chord-packed fret 16..31, which real-3DS
        // listening shows still produces a short damped transient.
        if ((suppressedMask & (1 << stringIndex)) != 0) return;

        int packedFret = (int)((encoded >> (5 * stringIndex)) & 31);
        bool chordMuted = packedFret >= 16;
        byte midiNote = chordMuted
            ? checked((byte)(BbpChordCatalog.GuitarTuning[stringIndex] + (packedFret & 0x0F)))
            : chord[stringIndex];
        if (midiNote is 0 or >= 128) return;

        long songEnd = FrameToTick(lengthFrames);
        if (attackTick >= songEnd) return;

        if (chordMuted)
        {
            // P-proven mute semantics use the ordinary fretted pitch/sample,
            // but attenuate the note-local attack to floor(normal * 47 / 128),
            // force Decay >= 99 and Sustain = 0. Portable MIDI cannot express
            // that per-note envelope override, so the one-frame gate remains an
            // approximation while the attack attenuation is now exact. If the
            // same strum already has a ringing copy of this MIDI pitch, omit the
            // damped duplicate so its same-channel Note Off cannot choke it.
            if (ringingPitches.Contains(midiNote)) return;
            byte mutedVelocity = (byte)((velocity * 47) / 128);
            output.Add(new NoteSpec(
                attackTick,
                Math.Max(1, Math.Min(TicksPerFrame, songEnd - attackTick)),
                midiNote,
                mutedVelocity));
            return;
        }

        output.Add(new NoteSpec(attackTick, Math.Max(1, songEnd - attackTick), midiNote, velocity));
        active.Add(output.Count - 1);
    }

    private static GuitarStuff PaletteAt(IReadOnlyList<GuitarStuff> palettes, int frame)
    {
        GuitarStuff current = palettes[0];
        for (int i = 1; i < palettes.Count; i++)
        {
            if (palettes[i].time > frame) break;
            current = palettes[i];
        }
        return current;
    }

    private static IEnumerable<ScoreSlot> EnumerateScoreSlots(byte[] notes, int lengthFrames, byte tripletMarker)
    {
        for (int cell = 0; cell * 4 + 4 <= notes.Length && cell * 12 < lengthFrames; cell++)
        {
            bool triplet = notes[cell * 4] == tripletMarker;
            int frame = cell * 12;
            int start = triplet ? 1 : 0;
            int step = triplet ? 4 : 3;

            for (int i = start; i < 4; i++, frame += step)
            {
                if (frame >= lengthFrames) yield break;
                int sourceIndex = cell * 4 + i;
                yield return new ScoreSlot(frame, sourceIndex, notes[sourceIndex]);
            }
        }
    }

    private static void BuildDrumNotes(
        ChannelInfo info,
        PackedNoteStrengths strengths,
        byte[] notes,
        IReadOnlyList<byte> drumMap,
        int lengthFrames,
        List<NoteSpec> output)
    {
        Span<byte> low = stackalloc byte[4];
        Span<byte> high = stackalloc byte[4];

        for (int cell = 0; cell * 4 + 4 <= notes.Length && cell * 12 < lengthFrames; cell++)
        {
            for (int j = 0; j < 4; j++)
            {
                byte value = notes[cell * 4 + j];
                low[j] = (byte)(value & 0x0F);
                high[j] = (byte)(value >> 4);
            }

            EmitDrumSlots(
                info, strengths, low, cell * 12, cell * 4,
                drumMap, StrengthLane.DrumLow, lengthFrames, output);
            EmitDrumSlots(
                info, strengths, high, cell * 12, cell * 4,
                drumMap, StrengthLane.DrumHigh, lengthFrames, output);
        }
    }

    private static void EmitDrumSlots(
        ChannelInfo info,
        PackedNoteStrengths strengths,
        ReadOnlySpan<byte> slots,
        int firstFrame,
        int firstSourceIndex,
        IReadOnlyList<byte> drumMap,
        StrengthLane strengthLane,
        int lengthFrames,
        List<NoteSpec> output)
    {
        bool triplet = slots[0] == 0x0F;
        int frame = firstFrame;
        int start = triplet ? 1 : 0;
        int step = triplet ? 4 : 3;

        for (int i = start; i < 4; i++, frame += step)
        {
            if (frame >= lengthFrames)
                break;

            byte index = slots[i];
            byte note = index < drumMap.Count ? drumMap[index] : (byte)0;
            if (note > 0)
            {
                long tick = FrameToTick(frame);
                byte velocity = AttackVelocity(
                    info, strengths, firstSourceIndex + i, strengthLane);
                output.Add(new NoteSpec(tick, TicksPerFrame, note, velocity));
            }
        }
    }

    private static byte AttackVelocity(
        ChannelInfo info,
        PackedNoteStrengths strengths,
        int scorePosition,
        StrengthLane lane)
    {
        byte packedStrength = strengths.GetNibble(scorePosition);
        int strengthCode = lane switch
        {
            StrengthLane.DrumHigh => (packedStrength >> 2) & 0x03,
            _ => packedStrength & 0x03
        };

        byte[] curve = info.noteStrengthCurve < NoteStrengthCurves.Length
            ? NoteStrengthCurves[info.noteStrengthCurve]
            : NoteStrengthCurves[0];

        // BBP2MIDI 1.2: recover the Width/strength table directly. Static part
        // volume is CC7, not baked into the note's MIDI velocity.
        return curve[strengthCode];
    }

    private static byte GmDrumVelocity(byte intensity, byte partVolume, short scoreVolume)
    {
        double part = Math.Clamp(partVolume, (byte)0, (byte)127) / 127.0;
        double score = Math.Clamp((int)scoreVolume, 0, 127) / 127.0;
        return (byte)Math.Clamp((int)Math.Round(intensity * Math.Sqrt(part * score)), 0, 127);
    }

    private static MidiFile BuildFile(
        BBPRecord record,
        BbpMidiProfile profile,
        IReadOnlyList<BbpMidiChannelInfo> channels,
        IReadOnlyList<List<NoteSpec>> notesByChannel,
        int lengthFrames)
    {
        var chunks = new List<MidiChunk> { BuildTempoTrack(record, lengthFrames) };
        for (int source = 0; source < SourceChannelCount; source++)
        {
            BbpMidiChannelInfo part = channels[source];
            BbpMidiProfile.Instrument instrument = profile.GetInstrument(part.InstrumentId);
            byte channel = profile.GmCompatible ? part.CanonicalChannel : (byte)(part.PlaybackChannel % 16);
            byte port = profile.GmCompatible ? (byte)0 : (byte)(part.PlaybackChannel / 16);
            short pan = source < record.gak.pan.Length ? record.gak.pan[source].value : (short)0;
            TimeValuePair[] automation = source < record.gak.volumeChanges.Length
                ? record.gak.volumeChanges[source].changes ?? [] : [];
            byte staticVolume = source < record.gak.channelInfo.Length
                ? record.gak.channelInfo[source].volume : (byte)127;
            chunks.Add(BuildChannelTrack(part, instrument, notesByChannel[source],
                channel, port, pan, staticVolume, automation, profile.GmCompatible, FrameToTick(lengthFrames)));
        }
        return new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(TicksPerQuarterNote)
        };
    }

    private static TrackChunk BuildTempoTrack(BBPRecord record, int lengthFrames)
    {
        long lengthTicks = FrameToTick(lengthFrames);
        double bbpGain = Math.Clamp(1 + record.gak.masterGain / 128.0, 0, 1.5);
        int midiMaster = Math.Clamp((int)Math.Round(Math.Sqrt(bbpGain / 1.5) * 16383), 0, 16383);
        // F0 7F 7F 04 01 ll mm F7: Universal Real-Time Master Volume.
        // This *exactly* mirrors BBP2MIDI 1.2's SpessaSynth-calibrated mapping.
        var sysex = new NormalSysExEvent(
            [0x7F, 0x7F, 0x04, 0x01, (byte)(midiMaster & 0x7F), (byte)(midiMaster >> 7), 0xF7]);
        var events = new List<AbsoluteEvent>
        {
            new(0, -20, new SequenceTrackNameEvent(record.Title)),
            new(0, -19, sysex),
            new(lengthTicks, 20, new MarkerEvent("End"))
        };

        bool hasTempoAtZero = false;
        foreach (TimeValuePair change in record.gak.tempoChanges ?? [])
        {
            if (change.time < 0) break;
            if (change.value <= 0) continue;
            int frame = Math.Clamp(change.time, 0, lengthFrames);
            long time = FrameToTick(frame);
            long microseconds = Math.Clamp(60_000_000L / change.value,
                SetTempoEvent.MinMicrosecondsPerQuarterNote,
                SetTempoEvent.MaxMicrosecondsPerQuarterNote);
            events.Add(new AbsoluteEvent(time, -10, new SetTempoEvent(microseconds)));
            if (time == 0) hasTempoAtZero = true;
        }
        if (!hasTempoAtZero)
            events.Add(new AbsoluteEvent(0, -10, new SetTempoEvent(SetTempoEvent.DefaultMicrosecondsPerQuarterNote)));
        return ToTrackChunk(events);
    }

    private static TrackChunk BuildChannelTrack(
        BbpMidiChannelInfo part,
        BbpMidiProfile.Instrument instrument,
        IReadOnlyList<NoteSpec> notes,
        byte midiChannel, byte port, short pan, byte partVolume,
        IReadOnlyList<TimeValuePair> volumeChanges, bool gmCompatible, long songEndTick)
    {
        var events = new List<AbsoluteEvent>
        {
            new(0, -20, new SequenceTrackNameEvent($"Part {part.SourceChannel + 1}: {part.InstrumentName}")),
            new(0, -15, new InstrumentNameEvent(part.InstrumentName))
        };
        if (!gmCompatible)
            events.Add(new AbsoluteEvent(0, -19, new PortPrefixEvent(port)));

        // GM uses bank 0, non-GM may use the exact BBP.sf2 bank/program override.
        int bank = gmCompatible ? 0 : instrument.SfBank ?? 0;
        events.Add(new AbsoluteEvent(0, -12, new ControlChangeEvent((SevenBitNumber)0,
            (SevenBitNumber)((bank >> 7) & 0x7F)) { Channel = (FourBitNumber)midiChannel }));
        events.Add(new AbsoluteEvent(0, -11, new ControlChangeEvent((SevenBitNumber)32,
            (SevenBitNumber)(bank & 0x7F)) { Channel = (FourBitNumber)midiChannel }));
        byte? program = !gmCompatible ? instrument.SfProgram ?? instrument.MidiProgram : instrument.MidiProgram;
        if (program is byte programNumber && (!part.IsDrum || !gmCompatible))
            events.Add(new AbsoluteEvent(0, -10, new ProgramChangeEvent((SevenBitNumber)programNumber)
                { Channel = (FourBitNumber)midiChannel }));

        if (!part.IsDrum)
        {
            byte midiPan = (byte)Math.Clamp(64 + pan, 0, 127);
            events.Add(new AbsoluteEvent(0, -9, new ControlChangeEvent((SevenBitNumber)10,
                (SevenBitNumber)midiPan) { Channel = (FourBitNumber)midiChannel }));
        }
        // GM drum parts share channel 10, so CC7 must be neutral. Their part
        // volume AND per-score automation are baked into each hit below.
        byte cc7 = gmCompatible && part.IsDrum ? (byte)127 : partVolume;
        events.Add(new AbsoluteEvent(0, -8, new ControlChangeEvent((SevenBitNumber)7,
            (SevenBitNumber)cc7) { Channel = (FourBitNumber)midiChannel }));

        if (!gmCompatible || !part.IsDrum)
        {
            bool wroteInitial = false;
            foreach (TimeValuePair change in volumeChanges)
            {
                if (change.time < 0) break;
                int frame = Math.Max(0, (int)change.time);
                // Automation past the song's end would stretch the file (and the player's
                // "playing" state) by up to minutes of silence.
                if (FrameToTick(frame) > songEndTick) continue;
                events.Add(new AbsoluteEvent(FrameToTick(frame), -7,
                    new ControlChangeEvent((SevenBitNumber)11,
                        (SevenBitNumber)Math.Clamp((int)change.value, 0, 127))
                    { Channel = (FourBitNumber)midiChannel }));
                wroteInitial |= frame == 0;
            }
            if (!wroteInitial)
                events.Add(new AbsoluteEvent(0, -7,
                    new ControlChangeEvent((SevenBitNumber)11, SevenBitNumber.MaxValue)
                    { Channel = (FourBitNumber)midiChannel }));
        }
        short currentScoreVolume = 127;
        int automationIndex = 0;
        // Drum low/high lanes are decoded separately: sort by timestamp before
        // sampling CC11, or high-lane hits can inherit future automation.
        foreach (NoteSpec note in notes.OrderBy(n => n.Time))
        {
            byte velocity = note.Velocity;
            if (gmCompatible && part.IsDrum)
            {
                int frame = (int)(note.Time / TicksPerFrame);
                while (automationIndex < volumeChanges.Count && volumeChanges[automationIndex].time >= 0
                    && volumeChanges[automationIndex].time <= frame)
                {
                    currentScoreVolume = volumeChanges[automationIndex].value;
                    automationIndex++;
                }
                velocity = GmDrumVelocity(velocity, partVolume, currentScoreVolume);
                if (velocity == 0) continue; // zero Note On == Note Off in MIDI
            }
            events.Add(new AbsoluteEvent(note.Time, 10,
                new NoteOnEvent((SevenBitNumber)note.Note, (SevenBitNumber)velocity)
                { Channel = (FourBitNumber)midiChannel }));
            events.Add(new AbsoluteEvent(note.Time + note.Length, 0,
                new NoteOffEvent((SevenBitNumber)note.Note, SevenBitNumber.MinValue)
                { Channel = (FourBitNumber)midiChannel }));
        }
        if (!gmCompatible)
            events.Add(new AbsoluteEvent(songEndTick, 30,
                new ControlChangeEvent((SevenBitNumber)123, SevenBitNumber.MinValue)
                { Channel = (FourBitNumber)midiChannel }));
        else
            // Harmless marker so the file ends at the song's length, not at the last Note Off.
            events.Add(new AbsoluteEvent(songEndTick, 30,
                new ControlChangeEvent((SevenBitNumber)64, SevenBitNumber.MinValue)
                { Channel = (FourBitNumber)midiChannel }));
        return ToTrackChunk(events);
    }

    private static TrackChunk ToTrackChunk(IEnumerable<AbsoluteEvent> source)
    {
        long previous = 0;
        var events = new List<MidiEvent>();
        foreach (AbsoluteEvent item in source.OrderBy(e => e.Time).ThenBy(e => e.Order))
        {
            if (item.Time < previous)
                throw new InvalidOperationException("MIDI events are not ordered by time.");
            item.Event.DeltaTime = item.Time - previous;
            previous = item.Time;
            events.Add(item.Event);
        }
        return new TrackChunk(events);
    }

    private static void CloseNote(List<NoteSpec> output, int index, long releaseTick)
    {
        NoteSpec active = output[index];
        output[index] = active with { Length = Math.Max(1, releaseTick - active.Time) };
    }

    private static void CloseNotes(List<NoteSpec> output, List<int> active, long releaseTick)
    {
        foreach (int index in active)
            CloseNote(output, index, releaseTick);
        active.Clear();
    }

    private static long FrameToTick(int frame) => (long)frame * TicksPerFrame;

    private static byte SourceToNonPercussionChannel(int source)
        => source == GeneralMidiDrumChannel ? (byte)10 : checked((byte)source);

    private sealed record AbsoluteEvent(long Time, int Order, MidiEvent Event);
    private sealed record NoteSpec(long Time, long Length, byte Note, byte Velocity);
    private readonly record struct ScoreSlot(int Frame, int SourceIndex, byte Value);
    private enum StrengthLane { Melodic, DrumLow, DrumHigh }
}
