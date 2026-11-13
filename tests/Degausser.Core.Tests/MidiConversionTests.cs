using Melanchall.DryWetMidi.Interaction;

namespace Degausser.Core.Tests;

public class MidiConversionTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", name);

    [Fact]
    public void SyntheticScaleConvertsToExpectedMidiNotes()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        BbpMidiDocument midi = BbpToMidiConverter.Convert(record);

        Assert.Equal((short)480, BbpToMidiConverter.TicksPerQuarterNote);
        Assert.Equal(40, BbpToMidiConverter.TicksPerFrame);
        Assert.Equal(record.LengthFrames, midi.LengthFrames);
        Assert.Equal((long)midi.LengthFrames * 40, midi.LengthTicks);

        BbpMidiChannelInfo first = midi.Channels[0];
        Assert.Equal(0, first.SourceChannel);
        Assert.Equal(0, first.PlaybackChannel);
        Assert.Equal(0, first.CanonicalChannel);
        Assert.Equal(PlayType.Standard, first.PlayType);
        Assert.False(first.IsDrum);
        Assert.Contains("Piano", first.InstrumentName);
        Assert.False(first.IsUnused);
        Assert.Equal(10, midi.Channels.Count);
        Assert.All(midi.Channels.Skip(1), channel =>
        {
            Assert.True(channel.IsUnused);
            Assert.Equal("Unused", channel.InstrumentName);
        });

        var notes = midi.File.GetNotes().OrderBy(n => n.Time).ToArray();
        Assert.Equal(8, notes.Length);
        Assert.Equal(new byte[] { 60, 62, 64, 65, 67, 69, 71, 72 }, notes.Select(n => (byte)n.NoteNumber).ToArray());
        Assert.Equal(Enumerable.Range(0, 8).Select(i => (long)i * 12 * 40), notes.Select(n => n.Time));
        Assert.All(notes, n => Assert.Equal(12L * 40, n.Length));
        // Synthetic fixture uses strength nibble F -> low two bits 3 (strong),
        // curve 0 and channel volume 100. Automation/master are later gains and
        // therefore no longer alter Note On velocity. floor(115 * 100 / 127)=90.
        Assert.All(notes, n => Assert.Equal((byte)115, (byte)n.Velocity));
        Assert.Equal((sbyte)100, midi.MasterGainRaw);
        Assert.Equal(228.0 / 128.0, midi.MasterDisplayMultiplier);
        Assert.Equal(midi.MasterDisplayMultiplier, midi.MasterPlaybackGainApproximation);
        Assert.All(notes, n => Assert.Equal((byte)0, (byte)n.Channel));
    }

    [Fact]
    public void PianoPartPlaysPackedOriginalChordSimultaneously()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Piano;
        record.gak.channelInfo[0].noteStrengthCurve = 1;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 1; // 1-based pianoPair[0].
        record.gak.channelNotes[0].notes[1] = 0; // release three BBP frames later.
        SetStrengthNibble(record, 0, 0, 0);      // curve 1 weakest = 50
        record.gak.pianoPair[0] = new IndexRootPair { index = 0, rawRoot = 0xE0 };
        record.gak.pianoOrig[0] = 60 | (64 << 8) | (67 << 16) | (72 << 24);

        BbpMidiDocument midi = BbpToMidiConverter.Convert(record);
        var notes = midi.File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => (byte)n.NoteNumber)
            .ToArray();

        Assert.Equal(new byte[] { 60, 64, 67, 72 }, notes.Select(n => (byte)n.NoteNumber).ToArray());
        Assert.All(notes, n => Assert.Equal(0L, n.Time));
        Assert.All(notes, n => Assert.Equal(3L * BbpToMidiConverter.TicksPerFrame, n.Length));
        Assert.All(notes, n => Assert.Equal((byte)50, (byte)n.Velocity));
    }

    [Fact]
    public void GuitarPartDownStrumsLowToHighAtOneScoreTime()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Guitar;
        record.gak.channelInfo[0].noteStrengthCurve = 1;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x05; // first button, down-strum
        record.gak.channelNotes[0].notes[1] = 0;    // release at frame 3
        SetStrengthNibble(record, 0, 0, 0);         // curve 1 weakest = 50

        record.gak.guitarOrig[0] = 0; // six open strings: E4 B3 G3 D3 A2 E2
        for (int i = 0; i < record.gak.guitarTimer.Length; i++)
        {
            record.gak.guitarTimer[i].time = -1;
            record.gak.guitarTimer[i].pair = Enumerable.Range(0, 10)
                .Select(_ => new IndexRootPair { index = 0xFF, rawRoot = 0xFF })
                .ToArray();
        }
        record.gak.guitarTimer[0].time = 0;
        record.gak.guitarTimer[0].pair[1] = new IndexRootPair { index = 0, rawRoot = 0xEF };
        record.gak.guitarTimer[1].time = -1;
        record.gak.guitarNoteMetadata[0] = 0xC000;

        BbpMidiDocument midi = BbpToMidiConverter.Convert(record);
        var notes = midi.File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => n.Time)
            .ToArray();

        Assert.Equal(new byte[] { 40, 45, 50, 55, 59, 64 }, notes.Select(n => (byte)n.NoteNumber).ToArray());
        Assert.All(notes, n => Assert.Equal(0L, n.Time));
        Assert.All(notes, n => Assert.Equal(120L, n.Length));
        Assert.All(notes, n => Assert.Equal((byte)50, (byte)n.Velocity));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(31)]
    public void NativePGuitarCategorySevenChordUsesGuitarOrigBeforeLegacyDecoding(int index)
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Guitar;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x05; // first button, down-strum
        record.gak.channelNotes[0].notes[1] = 0;

        // E20F and E21F are deliberate. The native-P sample uses both, and
        // the ELF's playback branch treats category-7 IDs as direct custom-
        // chord slots. The old resolver instead mistook their low nibble F for
        // a migrated-DX built-in and produced one G4 rather than guitarOrig.
        record.gak.guitarOrig[index] = 0; // six open strings
        for (int i = 0; i < record.gak.guitarTimer.Length; i++)
        {
            record.gak.guitarTimer[i].time = -1;
            record.gak.guitarTimer[i].pair = Enumerable.Range(0, 10)
                .Select(_ => new IndexRootPair { index = 0xFF, rawRoot = 0xFF })
                .ToArray();
        }
        record.gak.guitarTimer[0].time = 0;
        record.gak.guitarTimer[0].pair[1] = new IndexRootPair { index = (byte)index, rawRoot = 0xE2 };
        record.gak.guitarTimer[1].time = -1;
        record.gak.guitarNoteMetadata[0] = 0xC000;

        var notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => n.Time)
            .ToArray();

        Assert.Equal(new byte[] { 40, 45, 50, 55, 59, 64 }, notes.Select(n => (byte)n.NoteNumber).ToArray());
    }

    [Theory]
    [InlineData(0x4041, "40,47,52,55,62,64")] // Em7, rotation 1
    [InlineData(0x8010, "43,50,55,58,62,67")] // Gm, rotation 0
    [InlineData(0xC432, "46,53,57,62,65,70")] // BbM7, rotation 2
    [InlineData(0x4431, "40,51,58,62,67,70")]  // EbM7, rotation 1; low E is a short muted E2 transient
    [InlineData(0xC401, "46,50,53,58,62,70")] // Bb, rotation 1
    [InlineData(0x0040, "40,48,55,58,63,67")]  // Cm7, rotation 0; low E is a short muted E2 transient
    public void NativePBuiltInGuitarChordUsesGtChordVoicing(int chordId, string expectedLowToHighCsv)
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Guitar;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x05; // first button, down-strum
        record.gak.channelNotes[0].notes[1] = 0;

        for (int i = 0; i < record.gak.guitarTimer.Length; i++)
        {
            record.gak.guitarTimer[i].time = -1;
            record.gak.guitarTimer[i].pair = Enumerable.Range(0, 10)
                .Select(_ => new IndexRootPair { index = 0xFF, rawRoot = 0xFF })
                .ToArray();
        }

        record.gak.guitarTimer[0].time = 0;
        record.gak.guitarTimer[0].pair[1] = new IndexRootPair
        {
            index = (byte)chordId,
            rawRoot = (byte)(chordId >> 8)
        };
        record.gak.guitarTimer[1].time = -1;
        record.gak.guitarNoteMetadata[0] = 0xC000;

        var notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => n.Time)
            .ToArray();

        byte[] expectedLowToHigh = expectedLowToHighCsv.Split(',').Select(byte.Parse).ToArray();
        Assert.Equal(expectedLowToHigh, notes.Select(n => (byte)n.NoteNumber).ToArray());
    }

    [Fact]
    public void GuitarMetadataMutesStringsAndUpStrumReversesDirection()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Guitar;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x07; // first button, up-strum
        record.gak.channelNotes[0].notes[1] = 0;

        record.gak.guitarOrig[0] = 0;
        for (int i = 0; i < record.gak.guitarTimer.Length; i++)
        {
            record.gak.guitarTimer[i].time = -1;
            record.gak.guitarTimer[i].pair = Enumerable.Range(0, 10)
                .Select(_ => new IndexRootPair { index = 0xFF, rawRoot = 0xFF })
                .ToArray();
        }
        record.gak.guitarTimer[0].time = 0;
        record.gak.guitarTimer[0].pair[1] = new IndexRootPair { index = 0, rawRoot = 0xEF };
        record.gak.guitarNoteMetadata[0] = 0x8001; // attack + up + suppress high E (string bit 0)

        var notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => n.Time)
            .ToArray();

        Assert.Equal(new byte[] { 59, 55, 50, 45, 40 }, notes.Select(n => (byte)n.NoteNumber).ToArray());
        // Native playback submits every sounding string in one sequencer update;
        // the up/down stroke changes ordering, not authored onset time.
        Assert.All(notes, n => Assert.Equal(0L, n.Time));
    }


    [Fact]
    public void GuitarHoldBytesExtendChordUntilRest()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Guitar;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x05; // attack
        record.gak.channelNotes[0].notes[1] = 0x85; // same button + 0x80: hold
        record.gak.channelNotes[0].notes[2] = 0x85; // hold again
        record.gak.channelNotes[0].notes[3] = 0x00; // release at frame 9

        record.gak.guitarOrig[0] = 0;
        for (int i = 0; i < record.gak.guitarTimer.Length; i++)
        {
            record.gak.guitarTimer[i].time = -1;
            record.gak.guitarTimer[i].pair = Enumerable.Range(0, 10)
                .Select(_ => new IndexRootPair { index = 0xFF, rawRoot = 0xFF })
                .ToArray();
        }
        record.gak.guitarTimer[0].time = 0;
        record.gak.guitarTimer[0].pair[1] = new IndexRootPair { index = 0, rawRoot = 0xEF };
        record.gak.guitarNoteMetadata[0] = 0xC000;

        var notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => n.Time)
            .ToArray();

        Assert.Equal(6, notes.Length);
        long releaseTick = 9L * BbpToMidiConverter.TicksPerFrame;
        Assert.All(notes, note => Assert.Equal(releaseTick, note.Time + note.Length));
    }


    [Theory]
    [InlineData(0x06, 0x10, "55,60,64")] // C, native 3-voice rotation 6
    [InlineData(0x06, 0x90, "55,59,62")] // G, native 3-voice rotation 6
    public void NativePPianoPairUsesRecoveredChordGenerator(int index, int rawRoot, string expectedCsv)
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Piano;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 1;
        record.gak.channelNotes[0].notes[1] = 0;
        record.gak.pianoPair[0] = new IndexRootPair { index = (byte)index, rawRoot = (byte)rawRoot };

        var notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .Select(n => (byte)n.NoteNumber)
            .Order()
            .ToArray();

        Assert.Equal(expectedCsv.Split(',').Select(byte.Parse).ToArray(), notes);
    }

    [Theory]
    [InlineData(0xE0)]
    [InlineData(0xF9)]
    [InlineData(0xFF)]
    public void PianoCategorySevenSelectorsAllUseLiteralOriginalTable(int rawRoot)
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Piano;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 1;
        record.gak.channelNotes[0].notes[1] = 0;
        record.gak.pianoPair[0] = new IndexRootPair { index = 17, rawRoot = (byte)rawRoot };
        record.gak.pianoOrig[17] = 64 | (76 << 8);

        byte[] notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .Select(n => (byte)n.NoteNumber)
            .Order()
            .ToArray();

        Assert.Equal(new byte[] { 64, 76 }, notes);
    }

    [Fact]
    public void GuitarHighBitContinuationCanRetriggerFromMetadata()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Guitar;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x05;
        record.gak.channelNotes[0].notes[1] = 0x85; // same chord/button, high-bit continuation
        record.gak.channelNotes[0].notes[2] = 0;

        ConfigureSingleCustomGuitarChord(record, 0);
        record.gak.guitarNoteMetadata[0] = 0xC000; // fresh down-strum
        record.gak.guitarNoteMetadata[1] = 0xC000; // fresh down-strum inside continuation

        var notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => n.Time)
            .ThenBy(n => (byte)n.NoteNumber)
            .ToArray();

        Assert.Equal(12, notes.Length);
        Assert.Equal(6, notes.Count(n => n.Time == 0));
        Assert.Equal(6, notes.Count(n => n.Time == 3L * BbpToMidiConverter.TicksPerFrame));
        Assert.All(notes.Where(n => n.Time == 0), n => Assert.Equal(3L * BbpToMidiConverter.TicksPerFrame, n.Length));
        Assert.All(notes.Where(n => n.Time == 3L * BbpToMidiConverter.TicksPerFrame), n => Assert.Equal(3L * BbpToMidiConverter.TicksPerFrame, n.Length));
    }

    [Fact]
    public void GuitarEventMaskCanHitOnlyTheTwoMiddleStrings()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Guitar;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x05;
        record.gak.channelNotes[0].notes[1] = 0;

        ConfigureSingleCustomGuitarChord(record, 0); // six open strings
        record.gak.guitarNoteMetadata[0] = 0xC033; // suppress E/B/A/E; hit G and D

        byte[] notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => n.Time)
            .Select(n => (byte)n.NoteNumber)
            .ToArray();

        Assert.Equal(new byte[] { 50, 55 }, notes); // downstroke: D3 then G3
    }

    [Fact]
    public void ChordMutedGuitarStringsEmitOneFrameSameInstrumentTransients()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Guitar;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x05;
        record.gak.channelNotes[0].notes[1] = 0;

        uint allOpenMuted = PackGuitarFrets(16, 16, 16, 16, 16, 16);
        ConfigureSingleCustomGuitarChord(record, unchecked((int)allOpenMuted));
        record.gak.guitarNoteMetadata[0] = 0xC000;

        var notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .OrderBy(n => n.Time)
            .ToArray();

        Assert.Equal(new byte[] { 40, 45, 50, 55, 59, 64 }, notes.Select(n => (byte)n.NoteNumber).ToArray());
        Assert.All(notes, n => Assert.Equal((long)BbpToMidiConverter.TicksPerFrame, n.Length));
        Assert.All(notes, n => Assert.Equal((byte)42, (byte)n.Velocity)); // floor(115 * 47 / 128)
    }

    [Fact]
    public void PianoLiteralChordPreservesDoubledPitchVoices()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Piano;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 1;
        record.gak.channelNotes[0].notes[1] = 0;
        record.gak.pianoPair[0] = new IndexRootPair { index = 0, rawRoot = 0xE0 };
        record.gak.pianoOrig[0] = 60 | (60 << 8) | (67 << 16) | (72 << 24);

        byte[] notes = BbpToMidiConverter.Convert(record).File.GetNotes()
            .Where(n => (byte)n.Channel == 0)
            .Select(n => (byte)n.NoteNumber)
            .Order()
            .ToArray();

        Assert.Equal(new byte[] { 60, 60, 67, 72 }, notes);
    }

    [Fact]
    public void LegacyIntensityEffectNoLongerChangesNativeStrengthCurve()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].volume = 127;
        record.gak.channelInfo[0].noteStrengthCurve = 1;
        record.gak.volumeChanges[0].changes[0].value = 127;
        record.gak.masterGain = 64;
        SetStrengthNibble(record, 0, 0, 0); // native Width Large value 50

#pragma warning disable CS0618 // Deliberately exercising the obsolete option.
        byte flat = (byte)BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions { IntensityEffect = 0 }).File.GetNotes().First().Velocity;
        byte quarter = (byte)BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions { IntensityEffect = 0.25 }).File.GetNotes().First().Velocity;
        byte full = (byte)BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions { IntensityEffect = 1 }).File.GetNotes().First().Velocity;
#pragma warning restore CS0618

        Assert.Equal((byte)50, flat);
        Assert.Equal((byte)50, quarter);
        Assert.Equal((byte)50, full);
    }

    [Fact]
    public void MasterDisplayMultiplierUsesThreeDsVisibleOnePlusRawOver128Law()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].volume = 127;
        record.gak.channelInfo[0].noteStrengthCurve = 1;
        record.gak.volumeChanges[0].changes[0].value = 127;
        SetStrengthNibble(record, 0, 0, 3); // curve 1 strong = 125

        record.gak.masterGain = unchecked((sbyte)0xE0); // -32 -> 0.75x
        BbpMidiDocument low = BbpToMidiConverter.Convert(record);

        record.gak.masterGain = 0; // 1.00x
        BbpMidiDocument midpoint = BbpToMidiConverter.Convert(record);

        record.gak.masterGain = 64; // 1.50x
        BbpMidiDocument high = BbpToMidiConverter.Convert(record);

        Assert.Equal((byte)125, (byte)low.File.GetNotes().First().Velocity);
        Assert.Equal((byte)125, (byte)midpoint.File.GetNotes().First().Velocity);
        Assert.Equal((byte)125, (byte)high.File.GetNotes().First().Velocity);
        Assert.Equal((sbyte)-32, low.MasterGainRaw);
        Assert.Equal((sbyte)0, midpoint.MasterGainRaw);
        Assert.Equal((sbyte)64, high.MasterGainRaw);
        Assert.Equal(0.75, low.MasterDisplayMultiplier);
        Assert.Equal(1.00, midpoint.MasterDisplayMultiplier);
        Assert.Equal(1.50, high.MasterDisplayMultiplier);
        Assert.Equal(low.MasterDisplayMultiplier, low.MasterPlaybackGainApproximation);
        Assert.Equal(midpoint.MasterDisplayMultiplier, midpoint.MasterPlaybackGainApproximation);
        Assert.Equal(high.MasterDisplayMultiplier, high.MasterPlaybackGainApproximation);
    }

    [Theory]
    [InlineData(0, 0, 70)]
    [InlineData(0, 1, 85)]
    [InlineData(0, 2, 100)]
    [InlineData(0, 3, 115)]
    [InlineData(1, 0, 50)]
    [InlineData(1, 1, 75)]
    [InlineData(1, 2, 100)]
    [InlineData(1, 3, 125)]
    [InlineData(2, 0, 80)]
    [InlineData(2, 1, 90)]
    [InlineData(2, 2, 100)]
    [InlineData(2, 3, 110)]
    public void PerNoteStrengthUsesRecoveredNativeCurve(int curve, int strength, int expectedVelocity)
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].volume = 127;
        record.gak.channelInfo[0].noteStrengthCurve = (byte)curve;
        record.gak.volumeChanges[0].changes[0].value = 127;
        record.gak.masterGain = 64;
        SetStrengthNibble(record, 0, 0, (byte)strength);

        byte velocity = (byte)BbpToMidiConverter.Convert(record).File.GetNotes().First().Velocity;

        Assert.Equal((byte)expectedVelocity, velocity);
    }

    [Fact]
    public void DrumStrengthNibbleUsesIndependentLowAndHighLaneStrengths()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Drum;
        record.gak.channelInfo[0].instrument = 42; // Rock Drums
        record.gak.channelInfo[0].volume = 127;
        record.gak.channelInfo[0].noteStrengthCurve = 0;
        record.gak.volumeChanges[0].changes[0].value = 127;
        record.gak.masterGain = 64;
        Array.Clear(record.gak.channelNotes[0].notes);

        // Low score lane = pad 1, high score lane = pad 2 at the same frame.
        record.gak.channelNotes[0].notes[0] = 0x21;
        // Matching strength packing: low lane code 0, high lane code 3.
        SetStrengthNibble(record, 0, 0, 0x0C);

        var notes = BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions { Profile = BbpMidiProfile.GeneralMidi }).File.GetNotes()
            .Where(n => (byte)n.Channel == 9)
            .ToDictionary(n => (byte)n.NoteNumber, n => (byte)n.Velocity);

        Assert.Equal((byte)70, notes[35]); // B / low lane, weakest
        Assert.Equal((byte)115, notes[38]); // A / high lane, strongest
    }

    [Fact]
    public void MelodicVolumeAutomationUsesCc11WithoutRewritingNoteVelocity()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].volume = 127;
        record.gak.channelInfo[0].noteStrengthCurve = 0;
        record.gak.masterGain = 64;
        SetStrengthNibble(record, 0, 0, 2); // native attack 100
        record.gak.volumeChanges[0].changes[0] = new TimeValuePair { time = 0, value = 127 };
        record.gak.volumeChanges[0].changes[1] = new TimeValuePair { time = 6, value = 32 };
        record.gak.volumeChanges[0].changes[2] = new TimeValuePair { time = -1, value = 0 };

        BbpMidiDocument midi = BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions { Profile = BbpMidiProfile.GeneralMidi });
        Assert.Equal((byte)100, (byte)midi.File.GetNotes().First().Velocity);

        var controls = midi.File.GetTimedEvents()
            .Where(t => t.Event is Melanchall.DryWetMidi.Core.ControlChangeEvent e && (byte)e.Channel == 0)
            .Select(t => (t.Time, Event: (Melanchall.DryWetMidi.Core.ControlChangeEvent)t.Event))
            .ToArray();

        var cc7 = Assert.Single(controls, x => (byte)x.Event.ControlNumber == 7);
        Assert.Equal(0L, cc7.Time);
        Assert.Equal((byte)127, (byte)cc7.Event.ControlValue);
        var cc11 = controls.Where(x => (byte)x.Event.ControlNumber == 11).ToArray();
        Assert.Equal(2, cc11.Length);
        Assert.Equal(0L, cc11[0].Time);
        Assert.Equal((byte)127, (byte)cc11[0].Event.ControlValue);
        Assert.Equal(6L * BbpToMidiConverter.TicksPerFrame, cc11[1].Time);
        Assert.Equal((byte)32, (byte)cc11[1].Event.ControlValue);
    }

    [Fact]
    public void DrumVolumeAutomationIsBakedPerHitBecauseDrumPartsShareChannelTen()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Drum;
        record.gak.channelInfo[0].instrument = 42;
        record.gak.channelInfo[0].volume = 127;
        record.gak.channelInfo[0].noteStrengthCurve = 0;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x01; // pad 1 at frame 0
        record.gak.channelNotes[0].notes[1] = 0x01; // pad 1 at frame 3
        SetStrengthNibble(record, 0, 0, 2);
        SetStrengthNibble(record, 0, 1, 2);
        record.gak.volumeChanges[0].changes[0] = new TimeValuePair { time = 0, value = 127 };
        record.gak.volumeChanges[0].changes[1] = new TimeValuePair { time = 3, value = 32 };
        record.gak.volumeChanges[0].changes[2] = new TimeValuePair { time = -1, value = 0 };

        var notes = BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions { Profile = BbpMidiProfile.GeneralMidi }).File.GetNotes()
            .Where(n => (byte)n.Channel == 9)
            .OrderBy(n => n.Time)
            .ToArray();

        Assert.Equal(2, notes.Length);
        Assert.Equal((byte)100, (byte)notes[0].Velocity);
        Assert.Equal((byte)Math.Round(100.0 * Math.Sqrt(32.0 / 127.0)), (byte)notes[1].Velocity);
    }

    [Fact]
    public void DrumAutomationDefaultsToFullGainUntilItsFirstTimedChange()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].playType = PlayType.Drum;
        record.gak.channelInfo[0].instrument = 42;
        record.gak.channelInfo[0].volume = 127;
        record.gak.channelInfo[0].noteStrengthCurve = 0;
        Array.Clear(record.gak.channelNotes[0].notes);
        record.gak.channelNotes[0].notes[0] = 0x01;
        record.gak.channelNotes[0].notes[1] = 0x01;
        SetStrengthNibble(record, 0, 0, 2);
        SetStrengthNibble(record, 0, 1, 2);
        record.gak.volumeChanges[0].changes[0] = new TimeValuePair { time = 3, value = 32 };
        record.gak.volumeChanges[0].changes[1] = new TimeValuePair { time = -1, value = 0 };

        var notes = BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions { Profile = BbpMidiProfile.GeneralMidi }).File.GetNotes()
            .Where(n => (byte)n.Channel == 9)
            .OrderBy(n => n.Time)
            .ToArray();

        Assert.Equal(2, notes.Length);
        Assert.Equal((byte)100, (byte)notes[0].Velocity);
        Assert.Equal((byte)Math.Round(100.0 * Math.Sqrt(32.0 / 127.0)), (byte)notes[1].Velocity);
    }

    [Fact]
    public void RecoveredPanIsWrittenAsMidiPanControl()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.pan[0].value = -47;

        BbpMidiDocument midi = BbpToMidiConverter.Convert(record);
        var panEvents = midi.File.GetTimedEvents()
            .Select(timed => timed.Event)
            .OfType<Melanchall.DryWetMidi.Core.ControlChangeEvent>()
            .Where(e => (byte)e.Channel == 0 && (byte)e.ControlNumber == 10)
            .ToArray();

        var pan = Assert.Single(panEvents);
        Assert.Equal((byte)17, (byte)pan.ControlValue);
    }

    [Fact]
    public void CanonicalMidiCanBeWrittenAndReadBack()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        BbpMidiDocument midi = BbpToMidiConverter.Convert(record);

        using var stream = new MemoryStream();
        midi.File.Write(stream);
        stream.Position = 0;
        var reread = Melanchall.DryWetMidi.Core.MidiFile.Read(stream);

        Assert.Equal(midi.File.GetNotes().Count(), reread.GetNotes().Count());
        Assert.Equal(
            midi.File.GetNotes().Select(n => ((byte)n.Channel, (byte)n.NoteNumber, n.Time, n.Length)).ToArray(),
            reread.GetNotes().Select(n => ((byte)n.Channel, (byte)n.NoteNumber, n.Time, n.Length)).ToArray());
    }

    [Fact]
    public void InstrumentCatalogueUsesPortableMappingsAndRanges()
    {
        var piano = BbpInstrumentCatalog.Get(1);
        Assert.Equal("Piano", piano.Name);
        Assert.Equal((byte)1, piano.MidiProgram);
        Assert.False(piano.IsDrum);

        var drums = BbpInstrumentCatalog.Get(42);
        Assert.Equal("Rock Drums", drums.Name);
        Assert.True(drums.IsDrum);

        var portableDrumMappings = new Dictionary<int, byte[]>
        {
            [42] = [0, 35, 38, 37, 54, 42, 47, 45, 48, 46, 49],
            [43] = [0, 35, 40, 51, 54, 42, 47, 45, 48, 46, 49],
            [44] = [0, 35, 40, 37, 54, 70, 47, 45, 48, 46, 49],
            [45] = [0, 35, 38, 81, 38, 52, 54, 38, 54, 55, 49],
            [46] = [0, 61, 60, 80, 81, 54, 75, 39, 69, 66, 65],
            [47] = [0, 64, 63, 62, 58, 55, 70, 75, 56, 77, 76],
            [48] = [0, 54, 38, 61, 57, 75, 64, 67, 45, 46, 52],
            [194] = [0, 35, 38, 37, 54, 42, 47, 45, 48, 46, 49],
            [195] = [0, 35, 38, 37, 54, 42, 47, 45, 48, 46, 49],
            [196] = [0, 35, 38, 37, 54, 42, 47, 45, 48, 46, 49],
        };
        foreach ((int instrumentId, byte[] expected) in portableDrumMappings)
            Assert.Equal(expected, BbpInstrumentCatalog.Get(instrumentId).DrumNotes);

        // Instrument ranges in the XML alias historical GT/KB entries.
        Assert.Equal(BbpInstrumentCatalog.Get(1).Name, BbpInstrumentCatalog.Get(49).Name);
        Assert.Equal(BbpInstrumentCatalog.Get(131).Name, BbpInstrumentCatalog.Get(141).Name);

        var gtDxAliases = new Dictionary<int, int>
        {
            [197] = 34, [198] = 8, [199] = 138, [200] = 11,
            [201] = 140, [202] = 12, [203] = 13, [204] = 5,
            [205] = 16, [206] = 14, [207] = 37, [208] = 27,
            [209] = 25, [210] = 133, [211] = 22, [212] = 32
        };
        foreach ((int aliasId, int sourceId) in gtDxAliases)
        {
            BbpInstrumentInfo alias = BbpInstrumentCatalog.Get(aliasId);
            BbpInstrumentInfo source = BbpInstrumentCatalog.Get(sourceId);
            Assert.Equal(source.MidiProgram, alias.MidiProgram);
            Assert.Equal(source.DrumNotes, alias.DrumNotes);
            Assert.Contains(source.Name, alias.Name);
        }


        // The BBP.sf2-faithful player uses special nonzero banks for the three
        // timbres that cannot be represented exactly by the portable bank-0 map.
        var rockGuitar = BbpInstrumentCatalog.Get(10);
        Assert.Equal((byte)28, rockGuitar.MidiProgram);
        Assert.Equal((byte)16, rockGuitar.SoundFontBank);
        Assert.Equal((byte)30, rockGuitar.SoundFontProgram);

        var sine = BbpInstrumentCatalog.Get(164);
        Assert.Equal((byte)80, sine.MidiProgram);
        Assert.Equal((byte)8, sine.SoundFontBank);
        Assert.Equal((byte)80, sine.SoundFontProgram);

        var nesBass = BbpInstrumentCatalog.Get(167);
        Assert.Equal((byte)80, nesBass.MidiProgram);
        Assert.Equal((byte)2, nesBass.SoundFontBank);
        Assert.Equal((byte)80, nesBass.SoundFontProgram);

        // P-specific instruments now retain the recovered bank-0 choices rather
        // than the old placeholder piano mapping.
        Assert.Equal((byte)86, BbpInstrumentCatalog.Get(161).MidiProgram);
        Assert.Equal((byte)89, BbpInstrumentCatalog.Get(162).MidiProgram);
        Assert.Equal((byte)87, BbpInstrumentCatalog.Get(163).MidiProgram);
    }
    [Fact]
    public void PlaybackChannelCarriesBbpSoundFontOverrideMetadata()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.channelInfo[0].instrument = 10; // Rock Guitar
        record.gak.channelInfo[0].playType = PlayType.Standard;

        BbpMidiDocument midi = BbpToMidiConverter.Convert(record);
        BbpMidiChannelInfo channel = midi.Channels[0];

        Assert.Equal(10, channel.InstrumentId);
        Assert.Equal(BbpMidiProfile.Mosgis.GetInstrument(10).SfBank, channel.SoundFontBank);
        Assert.Equal(BbpMidiProfile.Mosgis.GetInstrument(10).SfProgram, channel.SoundFontProgram);
    }

    [Fact]
    public void ThreeFourSongUsesThirtySixFramesPerMeasure()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        record.gak.timeSignature = 3;

        BbpMidiDocument midi = BbpToMidiConverter.Convert(record);

        Assert.Equal(record.Lines * 36, record.LengthFrames);
        Assert.Equal(record.LengthFrames, midi.LengthFrames);
    }

    private static void ConfigureSingleCustomGuitarChord(BBPRecord record, int encoded)
    {
        record.gak.guitarOrig[0] = encoded;
        for (int i = 0; i < record.gak.guitarTimer.Length; i++)
        {
            record.gak.guitarTimer[i].time = -1;
            record.gak.guitarTimer[i].pair = Enumerable.Range(0, 10)
                .Select(_ => new IndexRootPair { index = 0xFF, rawRoot = 0xFF })
                .ToArray();
        }
        record.gak.guitarTimer[0].time = 0;
        record.gak.guitarTimer[0].pair[1] = new IndexRootPair { index = 0, rawRoot = 0xE0 };
        record.gak.guitarTimer[1].time = -1;
    }

    private static uint PackGuitarFrets(params int[] frets)
    {
        Assert.Equal(6, frets.Length);
        uint encoded = 0;
        for (int i = 0; i < frets.Length; i++)
            encoded |= ((uint)frets[i] & 31u) << (5 * i);
        return encoded;
    }

    private static void SetStrengthNibble(BBPRecord record, int channel, int scorePosition, byte nibble)
    {
        // Native order: even positions use the high nibble and odd positions the
        // low nibble of each stored byte, so little-endian 0x1234 reads 3,4,1,2.
        int wordIndex = scorePosition / 4;
        int shift = ((scorePosition & 3) >> 1) * 8 + ((scorePosition & 1) == 0 ? 4 : 0);
        ushort mask = (ushort)(0x0F << shift);
        ushort word = record.gak.noteStrengths[channel].words[wordIndex];
        record.gak.noteStrengths[channel].words[wordIndex] =
            (ushort)((word & ~mask) | ((nibble & 0x0F) << shift));
    }

}
