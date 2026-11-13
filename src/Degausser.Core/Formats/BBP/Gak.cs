using System.Diagnostics;
using Kermalis.EndianBinaryIO;

namespace Degausser;

/// <summary>
/// Decompressed Band Brothers P score (GAK). The format is a fixed-size
/// little-endian binary structure. Disk serialization is intentionally kept
/// separate from the public reverse-engineered model so semantic cleanup does
/// not depend on CLR struct layout.
/// </summary>
public class Gak
{
    public const int BinarySize = 0x14E3C;

    private sealed class DiskToneParameters
    {
        public DiskToneParameters() { }
        public byte Attack { get; set; }
        public byte Decay { get; set; }
        public byte Sustain { get; set; }
        public byte Release { get; set; }
        public byte Shape { get; set; }
        public byte Hold { get; set; }
        public byte Delay { get; set; }
        public byte Speed { get; set; }
        public byte Depth { get; set; }
        public byte Target { get; set; }
        public byte Dual { get; set; }
        public byte DualRate { get; set; }
        public byte BiquadType { get; set; }
        public byte BiquadValue { get; set; }
        public short LpfValue { get; set; }
    }

    private sealed class DiskChannelInfo
    {
        public DiskChannelInfo() { }
        public PlayType PlayType { get; set; }
        public byte Instrument { get; set; }
        public byte Volume { get; set; }
        public byte CloneID { get; set; }
        public byte MasterProStars { get; set; }
        public byte AmateurBeginnerStars { get; set; }
        public byte NoteStrengthCurve { get; set; }
        public byte PerformanceParam07 { get; set; }
        public byte PerformanceParam08 { get; set; }
        public byte DifficultyMetadata09 { get; set; }
        public byte VocalDifficultyMetadata { get; set; }
        public byte Reserved0B { get; set; }
        public uint Reserved0C0F { get; set; }
        public DiskToneParameters ToneA { get; set; } = new();
        public DiskToneParameters ToneB { get; set; } = new();
    }

    private sealed class DiskChannelNotes
    {
        public DiskChannelNotes() { }
        [BinaryArrayFixedLength(2400)]
        public byte[] Notes { get; set; } = new byte[2400];
    }

    private sealed class DiskPackedNoteStrengths
    {
        public DiskPackedNoteStrengths() { }
        [BinaryArrayFixedLength(600)]
        public ushort[] Words { get; set; } = new ushort[600];
    }

    private sealed class DiskTimeValuePair
    {
        public DiskTimeValuePair() { }
        public short Time { get; set; }
        public short Value { get; set; }
    }

    private sealed class DiskChanges512
    {
        public DiskChanges512() { }
        [BinaryArrayFixedLength(128)]
        public DiskTimeValuePair[] Changes { get; set; } = CreateArray(128, static () => new DiskTimeValuePair());
    }

    private sealed class DiskChanges1024
    {
        public DiskChanges1024() { }
        [BinaryArrayFixedLength(256)]
        public DiskTimeValuePair[] Changes { get; set; } = CreateArray(256, static () => new DiskTimeValuePair());
    }

    private sealed class DiskPanState
    {
        public DiskPanState() { }
        public short Zero1 { get; set; }
        public short Value { get; set; }
        public short MinusOne { get; set; }
        public short Zero2 { get; set; }
    }

    private sealed class DiskIndexRootPair
    {
        public DiskIndexRootPair() { }
        public byte Index { get; set; }
        public byte RawRoot { get; set; }
    }

    private sealed class DiskGuitarStuff
    {
        public DiskGuitarStuff() { }
        public short Time { get; set; }

        [BinaryArrayFixedLength(10)]
        public DiskIndexRootPair[] Pair { get; set; } = CreateArray(10, static () => new DiskIndexRootPair());
    }

    private sealed class DiskKeyStateTimeline
    {
        public DiskKeyStateTimeline() { }
        [BinaryArrayFixedLength(600)]
        public byte[] State { get; set; } = new byte[600];
    }

    // Property order here is the file layout. Offsets are documented in
    // docs/bbp-format.md and protected by byte-for-byte GAK tests.
    private sealed class DiskGak
    {
        public DiskGak() { }
        public int Version { get; set; }
        public int TitleID { get; set; }
        public int DateCreated { get; set; }
        public int DateModified { get; set; }

        [BinaryStringFixedLength(250)]
        [BinaryStringTrimNullTerminators]
        public string Title { get; set; } = string.Empty;

        public byte Unknown0204 { get; set; }
        public byte Reserved0205 { get; set; }
        public byte IsClassic { get; set; }
        public sbyte MelodyChannel { get; set; }
        public byte LinesPlus6 { get; set; }
        public byte TimeSignature { get; set; }
        public sbyte MasterGain { get; set; }
        public byte Reserved020B { get; set; }

        // 0x020C
        [BinaryArrayFixedLength(10)]
        public DiskChannelInfo[] ChannelInfo { get; set; } = CreateArray(10, static () => new DiskChannelInfo());

        // 0x03EC
        [BinaryArrayFixedLength(10)]
        public DiskChannelNotes[] ChannelNotes { get; set; } = CreateArray(10, static () => new DiskChannelNotes());

        // 0x61AC
        [BinaryArrayFixedLength(10)]
        public DiskPackedNoteStrengths[] NoteStrengths { get; set; } = CreateArray(10, static () => new DiskPackedNoteStrengths());

        // 0x908C
        [BinaryArrayFixedLength(128)]
        public DiskTimeValuePair[] TempoChanges { get; set; } = CreateArray(128, static () => new DiskTimeValuePair());

        // 0x928C
        [BinaryArrayFixedLength(10)]
        public DiskChanges1024[] VolumeChanges { get; set; } = CreateArray(10, static () => new DiskChanges1024());

        // 0xBA8C
        [BinaryArrayFixedLength(10)]
        public DiskChanges512[] RangeChanges { get; set; } = CreateArray(10, static () => new DiskChanges512());

        // 0xCE8C
        [BinaryArrayFixedLength(10)]
        public DiskChanges512[] TonePresetChanges { get; set; } = CreateArray(10, static () => new DiskChanges512());

        // 0xE28C
        [BinaryArrayFixedLength(10)]
        public DiskPanState[] Pan { get; set; } = CreateArray(10, static () => new DiskPanState());

        // 0xE2DC
        [BinaryArrayFixedLength(32)]
        public int[] GuitarOrig { get; set; } = new int[32];

        // 0xE35C
        [BinaryArrayFixedLength(48)]
        public DiskGuitarStuff[] GuitarTimer { get; set; } = CreateArray(48, static () => new DiskGuitarStuff());

        // 0xE77C. Earlier Degausser called this long[600]; reverse engineering
        // established it is one u16 metadata value per 2400-byte guitar note stream.
        [BinaryArrayFixedLength(2400)]
        public ushort[] GuitarNoteMetadata { get; set; } = new ushort[2400];

        // 0xFA3C
        [BinaryArrayFixedLength(120)]
        public DiskIndexRootPair[] PianoPair { get; set; } = CreateArray(120, static () => new DiskIndexRootPair());

        // 0xFB2C
        [BinaryArrayFixedLength(64)]
        public int[] PianoOrig { get; set; } = new int[64];

        // 0xFC2C
        [BinaryStringFixedLength(3000)]
        [BinaryStringTrimNullTerminators]
        public string KaraokeLyrics { get; set; } = string.Empty;

        // 0x1139C
        [BinaryArrayFixedLength(3000)]
        public short[] KaraokeTimer { get; set; } = new short[3000];

        // 0x12B0C
        [BinaryArrayFixedLength(11)]
        public DiskKeyStateTimeline[] KeyState { get; set; } = CreateArray(11, static () => new DiskKeyStateTimeline());

        // 0x144D4
        public int PianoChordChangesCount { get; set; }

        [BinaryArrayFixedLength(300)]
        public DiskTimeValuePair[] PianoChordChangeTable { get; set; } = CreateArray(300, static () => new DiskTimeValuePair());

        // 0x14988
        public int GuitarChordChangesCount { get; set; }

        [BinaryArrayFixedLength(300)]
        public DiskTimeValuePair[] GuitarChordChangeTable { get; set; } = CreateArray(300, static () => new DiskTimeValuePair());
    }

    public int version;
    public int titleID;
    public int dateCreated;
    public int dateModified;
    public string title = string.Empty;
    public byte unknown0204;
    public byte reserved0205; // GAK +0x205: reserved-zero in the 9,722-song official corpus.
    public byte isClassic; // GAK +0x206: inner classic/public-domain catalogue flag.
    public sbyte melodyChannel; // GAK +0x207: designated melody channel; -1 = none.
    public byte linesPlus6;
    public byte timeSignature;
    // Signed packed GAK +0x20A song-wide ALL/master control. Real-3DS editor
    // ground truth verifies the visible multiplier 1 + raw/128 (for example
    // raw +43 displays 1.33x). Do not confuse that UI mapping with a proven
    // final DSP amplitude law: external measurements suggest a slightly
    // stronger curve, but the native NintendoWare transfer is still unresolved.
    public sbyte masterGain;
    public byte reserved020B; // GAK +0x20B: reserved-zero in all 9,722 official BBPs.
    public ChannelInfo[] channelInfo = CreateArray(10, static () => CreateChannelInfo());
    public ChannelNotes[] channelNotes = CreateArray(10, static () => new ChannelNotes { notes = new byte[2400] });
    public PackedNoteStrengths[] noteStrengths = CreateArray(10, static () => new PackedNoteStrengths { words = new ushort[600] });
    public TimeValuePair[] tempoChanges = new TimeValuePair[128];
    public Changes1024[] volumeChanges = CreateArray(10, static () => new Changes1024 { changes = new TimeValuePair[256] });
    public Changes512[] rangeChanges = CreateArray(10, static () => new Changes512 { changes = new TimeValuePair[128] });
    public Changes512[] tonePresetChanges = CreateArray(10, static () => new Changes512 { changes = new TimeValuePair[128] });
    public PanState[] pan = new PanState[10];
    public int[] guitarOrig = new int[32];
    public GuitarStuff[] guitarTimer = CreateArray(48, static () => new GuitarStuff { pair = new IndexRootPair[10] });
    public ushort[] guitarNoteMetadata = new ushort[2400];
    public IndexRootPair[] pianoPair = new IndexRootPair[120];
    public int[] pianoOrig = new int[64];
    public string karaokeLyrics = string.Empty;
    public short[] karaokeTimer = new short[3000];
    public KeyStateTimeline[] keyState = CreateArray(11, static () => new KeyStateTimeline { state = new byte[600] });
    public int pianoChordChangesCount;
    public TimeValuePair[] pianoChordChangeTable = new TimeValuePair[300];
    public int guitarChordChangesCount;
    public TimeValuePair[] guitarChordChangeTable = new TimeValuePair[300];

    /// <summary>
    /// Native P considers karaoke present only when the lyric string is non-empty
    /// and the first timer entry is not the 0x3FFF unset sentinel.
    /// </summary>
    public bool HasKaraoke => !string.IsNullOrEmpty(karaokeLyrics)
        && karaokeTimer is { Length: > 0 }
        && unchecked((ushort)karaokeTimer[0]) != 0x3FFF;

    public static Gak Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BinarySize)
            throw new ArgumentException($"Gak requires {BinarySize} bytes.", nameof(bytes));

        using var stream = new MemoryStream(bytes[..BinarySize].ToArray(), writable: false);
        var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
        DiskGak disk = reader.ReadObject<DiskGak>();

        if (stream.Position != BinarySize)
            throw new InvalidDataException($"Gak parser consumed {stream.Position} bytes; expected {BinarySize}.");

        return FromDisk(disk);
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream(BinarySize);
        var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
        writer.WriteObject(ToDisk());

        byte[] result = stream.ToArray();
        if (result.Length != BinarySize)
            throw new InvalidDataException($"Gak serialized to {result.Length} bytes; expected {BinarySize}.");
        return result;
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length < BinarySize)
            throw new ArgumentException($"Gak requires {BinarySize} bytes.", nameof(destination));
        ToBytes().CopyTo(destination);
    }

    private static Gak FromDisk(DiskGak raw)
        => new()
        {
            version = raw.Version,
            titleID = raw.TitleID,
            dateCreated = raw.DateCreated,
            dateModified = raw.DateModified,
            title = raw.Title,
            unknown0204 = raw.Unknown0204,
            reserved0205 = raw.Reserved0205,
            isClassic = raw.IsClassic,
            melodyChannel = raw.MelodyChannel,
            linesPlus6 = raw.LinesPlus6,
            timeSignature = raw.TimeSignature,
            masterGain = raw.MasterGain,
            reserved020B = raw.Reserved020B,
            channelInfo = NormalizeArray(raw.ChannelInfo, 10).Select(FromDisk).ToArray(),
            channelNotes = NormalizeArray(raw.ChannelNotes, 10).Select(x => new ChannelNotes { notes = NormalizeArray(x.Notes, 2400) }).ToArray(),
            noteStrengths = NormalizeArray(raw.NoteStrengths, 10).Select(x => new PackedNoteStrengths { words = NormalizeArray(x.Words, 600) }).ToArray(),
            tempoChanges = NormalizeArray(raw.TempoChanges, 128).Select(FromDisk).ToArray(),
            volumeChanges = NormalizeArray(raw.VolumeChanges, 10).Select(x => new Changes1024
            {
                changes = NormalizeArray(x.Changes, 256).Select(FromDisk).ToArray()
            }).ToArray(),
            rangeChanges = NormalizeArray(raw.RangeChanges, 10).Select(x => new Changes512
            {
                changes = NormalizeArray(x.Changes, 128).Select(FromDisk).ToArray()
            }).ToArray(),
            tonePresetChanges = NormalizeArray(raw.TonePresetChanges, 10).Select(x => new Changes512
            {
                changes = NormalizeArray(x.Changes, 128).Select(FromDisk).ToArray()
            }).ToArray(),
            pan = NormalizeArray(raw.Pan, 10).Select(FromDisk).ToArray(),
            guitarOrig = NormalizeArray(raw.GuitarOrig, 32),
            guitarTimer = NormalizeArray(raw.GuitarTimer, 48).Select(FromDisk).ToArray(),
            guitarNoteMetadata = NormalizeArray(raw.GuitarNoteMetadata, 2400),
            pianoPair = NormalizeArray(raw.PianoPair, 120).Select(FromDisk).ToArray(),
            pianoOrig = NormalizeArray(raw.PianoOrig, 64),
            karaokeLyrics = raw.KaraokeLyrics,
            karaokeTimer = NormalizeArray(raw.KaraokeTimer, 3000),
            keyState = NormalizeArray(raw.KeyState, 11).Select(x => new KeyStateTimeline { state = NormalizeArray(x.State, 600) }).ToArray(),
            pianoChordChangesCount = raw.PianoChordChangesCount,
            pianoChordChangeTable = NormalizeArray(raw.PianoChordChangeTable, 300).Select(FromDisk).ToArray(),
            guitarChordChangesCount = raw.GuitarChordChangesCount,
            guitarChordChangeTable = NormalizeArray(raw.GuitarChordChangeTable, 300).Select(FromDisk).ToArray()
        };

    private DiskGak ToDisk()
        => new()
        {
            Version = version,
            TitleID = titleID,
            DateCreated = dateCreated,
            DateModified = dateModified,
            Title = NormalizeByValTStr(title, 250),
            Unknown0204 = unknown0204,
            Reserved0205 = reserved0205,
            IsClassic = isClassic,
            MelodyChannel = melodyChannel,
            LinesPlus6 = linesPlus6,
            TimeSignature = timeSignature,
            MasterGain = masterGain,
            Reserved020B = reserved020B,
            ChannelInfo = NormalizeArray(channelInfo, 10).Select(ToDisk).ToArray(),
            ChannelNotes = NormalizeArray(channelNotes, 10).Select(x => new DiskChannelNotes { Notes = NormalizeArray(x.notes, 2400) }).ToArray(),
            NoteStrengths = NormalizeArray(noteStrengths, 10).Select(x => new DiskPackedNoteStrengths { Words = NormalizeArray(x.words, 600) }).ToArray(),
            TempoChanges = NormalizeArray(tempoChanges, 128).Select(ToDisk).ToArray(),
            VolumeChanges = NormalizeArray(volumeChanges, 10).Select(x => new DiskChanges1024
            {
                Changes = NormalizeArray(x.changes, 256).Select(ToDisk).ToArray()
            }).ToArray(),
            RangeChanges = NormalizeArray(rangeChanges, 10).Select(x => new DiskChanges512
            {
                Changes = NormalizeArray(x.changes, 128).Select(ToDisk).ToArray()
            }).ToArray(),
            TonePresetChanges = NormalizeArray(tonePresetChanges, 10).Select(x => new DiskChanges512
            {
                Changes = NormalizeArray(x.changes, 128).Select(ToDisk).ToArray()
            }).ToArray(),
            Pan = NormalizeArray(pan, 10).Select(ToDisk).ToArray(),
            GuitarOrig = NormalizeArray(guitarOrig, 32),
            GuitarTimer = NormalizeArray(guitarTimer, 48).Select(ToDisk).ToArray(),
            GuitarNoteMetadata = NormalizeArray(guitarNoteMetadata, 2400),
            PianoPair = NormalizeArray(pianoPair, 120).Select(ToDisk).ToArray(),
            PianoOrig = NormalizeArray(pianoOrig, 64),
            KaraokeLyrics = NormalizeByValTStr(karaokeLyrics, 3000),
            KaraokeTimer = NormalizeArray(karaokeTimer, 3000),
            KeyState = NormalizeArray(keyState, 11).Select(x => new DiskKeyStateTimeline { State = NormalizeArray(x.state, 600) }).ToArray(),
            PianoChordChangesCount = pianoChordChangesCount,
            PianoChordChangeTable = NormalizeArray(pianoChordChangeTable, 300).Select(ToDisk).ToArray(),
            GuitarChordChangesCount = guitarChordChangesCount,
            GuitarChordChangeTable = NormalizeArray(guitarChordChangeTable, 300).Select(ToDisk).ToArray()
        };

    private static ChannelInfo FromDisk(DiskChannelInfo raw)
        => new()
        {
            playType = raw.PlayType,
            instrument = raw.Instrument,
            volume = raw.Volume,
            cloneID = raw.CloneID,
            masterProStars = raw.MasterProStars,
            amateurBeginnerStars = raw.AmateurBeginnerStars,
            noteStrengthCurve = raw.NoteStrengthCurve,
            performanceParam07 = raw.PerformanceParam07,
            performanceParam08 = raw.PerformanceParam08,
            difficultyMetadata09 = raw.DifficultyMetadata09,
            vocalDifficultyMetadata = raw.VocalDifficultyMetadata,
            reserved0B = raw.Reserved0B,
            reserved0C0F = raw.Reserved0C0F,
            toneA = FromDisk(raw.ToneA),
            toneB = FromDisk(raw.ToneB)
        };

    private static DiskChannelInfo ToDisk(ChannelInfo src)
        => new()
        {
            PlayType = src.playType,
            Instrument = src.instrument,
            Volume = src.volume,
            CloneID = src.cloneID,
            MasterProStars = src.masterProStars,
            AmateurBeginnerStars = src.amateurBeginnerStars,
            NoteStrengthCurve = src.noteStrengthCurve,
            PerformanceParam07 = src.performanceParam07,
            PerformanceParam08 = src.performanceParam08,
            DifficultyMetadata09 = src.difficultyMetadata09,
            VocalDifficultyMetadata = src.vocalDifficultyMetadata,
            Reserved0B = src.reserved0B,
            Reserved0C0F = src.reserved0C0F,
            ToneA = ToDisk(src.toneA),
            ToneB = ToDisk(src.toneB)
        };

    private static ToneParameters FromDisk(DiskToneParameters raw)
        => new()
        {
            attack = raw.Attack,
            decay = raw.Decay,
            sustain = raw.Sustain,
            release = raw.Release,
            shape = raw.Shape,
            hold = raw.Hold,
            delay = raw.Delay,
            speed = raw.Speed,
            depth = raw.Depth,
            target = raw.Target,
            dual = raw.Dual,
            dualRate = raw.DualRate,
            biquadType = raw.BiquadType,
            biquadValue = raw.BiquadValue,
            lpfValue = raw.LpfValue
        };

    private static DiskToneParameters ToDisk(ToneParameters src)
        => new()
        {
            Attack = src.attack,
            Decay = src.decay,
            Sustain = src.sustain,
            Release = src.release,
            Shape = src.shape,
            Hold = src.hold,
            Delay = src.delay,
            Speed = src.speed,
            Depth = src.depth,
            Target = src.target,
            Dual = src.dual,
            DualRate = src.dualRate,
            BiquadType = src.biquadType,
            BiquadValue = src.biquadValue,
            LpfValue = src.lpfValue
        };

    private static TimeValuePair FromDisk(DiskTimeValuePair raw) => new() { time = raw.Time, value = raw.Value };
    private static DiskTimeValuePair ToDisk(TimeValuePair src) => new() { Time = src.time, Value = src.value };

    private static PanState FromDisk(DiskPanState raw)
        => new() { zero1 = raw.Zero1, value = raw.Value, minusOne = raw.MinusOne, zero2 = raw.Zero2 };

    private static DiskPanState ToDisk(PanState src)
        => new() { Zero1 = src.zero1, Value = src.value, MinusOne = src.minusOne, Zero2 = src.zero2 };

    private static IndexRootPair FromDisk(DiskIndexRootPair raw) => new() { index = raw.Index, rawRoot = raw.RawRoot };
    private static DiskIndexRootPair ToDisk(IndexRootPair src) => new() { Index = src.index, RawRoot = src.rawRoot };

    private static GuitarStuff FromDisk(DiskGuitarStuff raw)
        => new()
        {
            time = raw.Time,
            pair = NormalizeArray(raw.Pair, 10).Select(FromDisk).ToArray()
        };

    private static DiskGuitarStuff ToDisk(GuitarStuff src)
        => new()
        {
            Time = src.time,
            Pair = NormalizeArray(src.pair, 10).Select(ToDisk).ToArray()
        };

    private static string NormalizeByValTStr(string? value, int sizeConst)
    {
        string text = value ?? string.Empty;
        int nul = text.IndexOf('\0');
        if (nul >= 0) text = text[..nul];

        // MarshalAs(UnmanagedType.ByValTStr, SizeConst=N) reserves one UTF-16
        // code unit for the terminating NUL. EndianBinaryIO's fixed-length
        // strings are exactly N code units, so enforce the historical limit
        // before serialization and let the writer pad the remainder with NUL.
        int maxChars = Math.Max(0, sizeConst - 1);
        if (text.Length <= maxChars) return text;

        int length = maxChars;
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;
        return text[..length];
    }

    private static T[] NormalizeArray<T>(T[]? source, int length)
    {
        var result = new T[length];
        if (source is not null)
            source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(result);
        return result;
    }

    private static T[] CreateArray<T>(int length, Func<T> factory)
        => Enumerable.Range(0, length).Select(_ => factory()).ToArray();

    private static ChannelInfo CreateChannelInfo()
        => new();
}

#region GAK domain structures
public enum PlayType : byte { Standard, Drum, Guitar, Piano }

[DebuggerDisplay("({attack}, {decay}, {sustain}, {release})")]
public struct ToneParameters
{
    public byte attack;
    public byte decay;
    public byte sustain;
    public byte release;
    public byte shape;
    public byte hold;
    public byte delay;
    public byte speed;
    public byte depth;
    public byte target;
    public byte dual;
    public byte dualRate;
    public byte biquadType;
    public byte biquadValue;
    public short lpfValue;
}

public struct ChannelInfo
{
    public PlayType playType;
    public byte instrument;
    public byte volume;
    public byte cloneID;
    public byte masterProStars;
    public byte amateurBeginnerStars;
    public byte noteStrengthCurve;
    public byte performanceParam07;
    public byte performanceParam08;
    public byte difficultyMetadata09;
    public byte vocalDifficultyMetadata;
    public byte reserved0B;
    public uint reserved0C0F;
    public ToneParameters toneA;
    public ToneParameters toneB;

    public readonly byte ProStars => (byte)(masterProStars & 0x0F);
    public readonly byte MasterStars => (byte)(masterProStars >> 4);
    public readonly byte BeginnerStars => (byte)(amateurBeginnerStars & 0x0F);
    public readonly byte AmateurStars => (byte)(amateurBeginnerStars >> 4);
    public readonly byte VocalStars => (byte)(vocalDifficultyMetadata & 0x0F);
}

public struct ChannelNotes
{
    public byte[] notes;
}

/// <summary>
/// GAK 0x061AC packed note strengths. Native playback walks the stored bytes
/// in memory order and reads each byte's high nibble before its low nibble.
/// Because GAK words are little-endian, one numeric u16 therefore yields
/// positions [bits 7..4, 3..0, 15..12, 11..8], not numeric high-to-low order.
/// Melodic/Piano/Guitar use the low two bits of each native-P nibble; drums use
/// the nibble as two independent two-bit lanes.
/// </summary>
public struct PackedNoteStrengths
{
    public ushort[] words;

    public readonly byte GetNibble(int scorePosition)
    {
        if ((uint)scorePosition >= 2400u || words is null || words.Length <= scorePosition / 4)
            return 0x0F;

        ushort word = words[scorePosition / 4];
        int positionInWord = scorePosition & 3;
        int byteShift = (positionInWord >> 1) * 8;
        int nibbleShift = (positionInWord & 1) == 0 ? 4 : 0;
        return (byte)((word >> (byteShift + nibbleShift)) & 0x0F);
    }
}

[DebuggerDisplay("({time}, {value})")]
public struct TimeValuePair
{
    public short time;
    public short value;
}

public struct Changes512
{
    public TimeValuePair[] changes;
}

public struct Changes1024
{
    public TimeValuePair[] changes;
}

public struct PanState
{
    public short zero1;
    public short value;
    public short minusOne;
    public short zero2;
}

[DebuggerDisplay("({index}, {rawRoot})")]
public struct IndexRootPair
{
    public byte index;
    public byte rawRoot;

    private static readonly byte[] MajorScale = [0, 2, 4, 5, 7, 9, 11];
    private enum Accidental { Natural, Sharp, Flat }

    public byte Root
    {
        get
        {
            if (rawRoot == 0xFF) return rawRoot;

            int degree = rawRoot & 0xF;
            byte note = degree < 7 ? MajorScale[degree] : (byte)(degree % 12);
            return (Accidental)(rawRoot >> 4) switch
            {
                Accidental.Sharp => ++note,
                Accidental.Flat => --note,
                _ => note
            };
        }
    }

    public short Short => (short)((rawRoot << 8) | index);
}

public struct GuitarStuff
{
    public short time;
    public IndexRootPair[] pair;
}

/// <summary>
/// GAK 0x12B0C notation/key-state block. The file stores eleven independent
/// 600-byte timelines; the historical int[150] declaration was only a byte-
/// count coincidence and obscured the per-step state representation.
/// </summary>
public struct KeyStateTimeline
{
    public byte[] state;
}
#endregion
