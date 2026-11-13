using Kermalis.EndianBinaryIO;
using System.Text;

namespace Degausser;

/// <summary>
/// Options for converting a Daigasso! Band Brothers DX BDX file into the
/// Band Brothers P score/container model used by Degausser.
/// </summary>
public enum BdxPianoChordEncoding
{
    /// <summary>
    /// Reproduce the DX engine's pitches exactly, then emit a native P chord
    /// descriptor only when P generates the same pitch multiset for the same
    /// chord quality. Otherwise use a literal pianoOrig chord.
    /// </summary>
    NativeWhenExact,

    /// <summary>
    /// Reproduce the DX engine's pitches exactly and always store built-in DX
    /// Piano chords as literal P pianoOrig voicings. Useful as a conservative
    /// sound-fidelity mode and for inspecting the native mapper.
    /// </summary>
    ExactLiteral,

    /// <summary>
    /// Preserve the pre-DX-ROM archaeology converter byte behaviour. This mode
    /// intentionally retains its historical Piano limitations and exists only
    /// for reference-corpus regression; new conversions should not use it.
    /// </summary>
    LegacyCompatibility
}

public sealed record BdxConversionOptions(
    uint? TitleId = null,
    // Overrides the BDX's own contributor name when set.
    string? Author = null,
    // Used only when the BDX has no contributor and no Author override is given.
    string? DefaultAuthor = null,
    bool ConfirmedGuitarAliases = true,
    BdxPianoChordEncoding PianoChordEncoding = BdxPianoChordEncoding.NativeWhenExact,
    // Adapt the song to P: Nintendo's measured DX-migration master gain, pan and
    // karaoke conventions, plus a scrolling-friendly karaoke layout.
    // False keeps the raw copy that the Python-oracle comparisons expect.
    bool NormaliseForP = true);

public sealed record BdxConversionResult(
    Gak Gak,
    JbMgr.Item ManagerItem,
    byte[] RawGak,
    IReadOnlyList<string> Warnings);

public sealed class BdxConversionException(string message) : IOException(message) { }

/// <summary>
/// BDX -&gt; BBP conversion grounded in the paired-corpus/BBP archaeology and,
/// for Piano/Guitar chord semantics, the original Japanese DX ARM9. The older
/// Python archaeology oracle remains available through LegacyCompatibility for
/// byte-for-byte regression, but is no longer the production Piano baseline.
/// </summary>
public static class BdxToBbpConverter
{
    /// <summary>Author written for a BDX that names no contributor, unless the caller picks one.
    /// The song's author field holds 17 UTF-16 units.</summary>
    public const string DefaultAuthor = "Degausser 3.0";
    public const int MaxAuthorLength = 17;

    internal static string DefaultAuthorFor(string? chosen)
    {
        string name = (chosen ?? string.Empty).Trim();
        if (name.Length == 0) return DefaultAuthor;
        return name.Length <= MaxAuthorLength ? name : name[..MaxAuthorLength];
    }

    public const int BdxSize = 0x8000;
    // The original DX save slot is 0x8000 bytes, but every converter-read song
    // field ends at 0x7F19. A few historical dumps omit the undocumented
    // 0x7F1A..0x7FFF tail entirely, so accept them and zero-pad to a canonical
    // slot. Conversely, some web-download artifacts have harmless bytes
    // appended after a complete 0x8000-byte BDX; ignore that suffix.
    public const int MinimumConvertibleBdxSize = 0x7F1A;

    private static ReadOnlySpan<byte> LegacyBbsMagic => "BBRS_GAK"u8;

    private const int BdxLabels = 0x0048;
    private const int BdxTimeSignature = 0x00AA;
    private const int BdxLinesPlus6 = 0x00AB;
    private const int BdxMainInstrument = 0x00AE;
    private const int BdxHasKaraoke = 0x00AF;
    private const int BdxMasterVolume = 0x00B7;
    private const int BdxDateCreated = 0x00BC;
    private const int BdxDateModified = 0x00C0;
    private const int BdxChannelInfo = 0x00C8;
    private const int BdxContributor = 0x014C;
    private const int BdxEnvelopes = 0x01E8;
    private const int BdxNotes = 0x0248;
    private const int BdxTempo = 0x4248;
    private const int BdxChanges = 0x42C8;
    private const int BdxGuitarOrig = 0x4AC8;
    private const int BdxGuitarTimer = 0x4B08;
    private const int BdxKaraokeLyrics = 0x4E08;
    private const int BdxKaraokeTimer = 0x5608;
    private const int BdxPianoPair = 0x6608;
    private const int BdxPianoOrig = 0x6648;
    private const int BdxPianoCustomFlags = 0x66C8;
    private const int BdxChordCount = 0x6918;
    private const int BdxChordTable = 0x691C;
    private const int BdxKeyBlocks = 0x6D18;
    private const int BdxPianoHighest = 0x7F18;
    private const int BdxPianoVoicing = 0x7F19;

    private const int PVersion = 0x0000;
    private const int PTitleId = 0x0004;
    private const int PDateCreated = 0x0008;
    private const int PDateModified = 0x000C;
    private const int PTitle = 0x0010;
    private const int PUnknown204 = 0x0204;
    private const int PMelody = 0x0207;
    private const int PLinesPlus6 = 0x0208;
    private const int PTimeSignature = 0x0209;
    private const int PMasterGain = 0x020A;
    private const int PReserved20B = 0x020B;
    private const int PChannelInfo = 0x020C;
    private const int PNotes = 0x03EC;
    private const int PNoteStrengths = 0x61AC;
    private const int PTempo = 0x908C;
    private const int PVolume = 0x928C;
    private const int PRange = 0xBA8C;
    private const int PEffect = 0xCE8C;
    private const int PPan = 0xE28C;
    private const int PGuitarOrig = 0xE2DC;
    private const int PGuitarTimer = 0xE35C;
    private const int PGuitarNoteMetadata = 0xE77C;
    private const int PPianoPair = 0xFA3C;
    private const int PPianoOrig = 0xFB2C;
    private const int PKaraokeLyrics = 0xFC2C;
    private const int PKaraokeTimer = 0x1139C;
    private const int PKeyBlocks = 0x12B0C;
    private const int PPianoChordCount = 0x144D4;
    private const int PPianoChordTable = 0x144D8;
    private const int PGuitarChordCount = 0x14988;
    private const int PGuitarChordTable = 0x1498C;

    private const byte PlayStandard = 0;
    private const byte PlayDrum = 1;
    private const byte PlayGuitar = 2;
    private const byte PlayPiano = 3;

    private const char ZeroWidthSpace = '​';
    private static readonly byte[] MajorScale = [0, 2, 4, 5, 7, 9, 11];


    public static BdxConversionResult Convert(ReadOnlySpan<byte> bdx, BdxConversionOptions? options = null)
    {
        byte[] source = NormalizeSource(bdx, out string? containerWarning);

        options ??= new BdxConversionOptions();
        uint titleId = options.TitleId ?? CreateDeterministicTitleId(source);
        var converter = new Converter(
            source,
            titleId,
            options.Author,
            options.DefaultAuthor,
            options.ConfirmedGuitarAliases,
            options.PianoChordEncoding,
            options.NormaliseForP);
        BdxConversionResult result = converter.Convert();
        if (containerWarning is null)
            return result;

        var warnings = new List<string>(result.Warnings.Count + 1) { containerWarning };
        warnings.AddRange(result.Warnings);
        return result with { Warnings = warnings.AsReadOnly() };
    }

    /// <summary>
    /// Returns true for the 0x6000-byte legacy BBRS_GAK/BBS song format that
    /// sometimes circulates with a misleading .bdx extension. It is not a BDX.
    /// </summary>
    public static bool IsLegacyBbs(ReadOnlySpan<byte> bytes)
        => bytes.Length >= LegacyBbsMagic.Length && bytes[..LegacyBbsMagic.Length].SequenceEqual(LegacyBbsMagic);

    /// <summary>
    /// Validate/normalise historical BDX container quirks without changing any
    /// documented song data. Short files are accepted only if all fields read by
    /// the converter are present; long files are truncated after the canonical
    /// 0x8000-byte DX slot.
    /// </summary>
    public static byte[] NormalizeSource(ReadOnlySpan<byte> bdx, out string? warning)
    {
        warning = null;
        if (IsLegacyBbs(bdx))
            throw new BdxConversionException(
                "This file starts with BBRS_GAK and is a legacy .bbs/GAK song, not a Band Brothers DX BDX. " +
                "Some old packs mislabeled these files with a .bdx extension.");

        if (bdx.Length < MinimumConvertibleBdxSize)
            throw new BdxConversionException(
                $"BDX size is 0x{bdx.Length:X}; at least 0x{MinimumConvertibleBdxSize:X} bytes are required " +
                "to contain all documented song fields used by the converter.");

        if (bdx.Length == BdxSize)
            return bdx.ToArray();

        var source = new byte[BdxSize];
        int copy = Math.Min(bdx.Length, BdxSize);
        bdx[..copy].CopyTo(source);

        if (bdx.Length < BdxSize)
        {
            warning =
                $"BDX dump is 0x{bdx.Length:X} bytes instead of the canonical 0x{BdxSize:X}; " +
                "the missing bytes are after all converter-read song fields and were zero-padded.";
        }
        else
        {
            warning =
                $"BDX dump has 0x{bdx.Length - BdxSize:X} byte(s) appended after the canonical 0x{BdxSize:X}-byte slot; " +
                "the suffix was ignored.";
        }

        return source;
    }

    public static uint CreateDeterministicTitleId(ReadOnlySpan<byte> bdx)
    {
        // Deliberately matches the Python reference: low 16 bits of standard
        // CRC-32, placed in the old 0x8000xxxx custom/editable title-ID range.
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bdx)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        }
        crc = ~crc;
        uint low = crc & 0xFFFF;
        if (low == 0) low = 1;
        return 0x80000000u | low;
    }

    /// <summary>The pre-2026-11 inferred table, kept for comparisons with the Python oracle.</summary>
    internal static int MapPanLegacy(int value)
    {
        int a = Math.Abs(value);
        int mapped = a <= 4 ? 0 : a <= 10 ? 20 : a <= 16 ? 32 : a <= 22 ? 47 : a <= 29 ? 57 : 62;
        return value < 0 ? -mapped : mapped;
    }

    public static int MapPan(int value)
    {
        if (value == 0) return 0;
        int sign = value < 0 ? -1 : 1;
        int a = Math.Abs(value);
        // Nintendo's DX -> P quantisation, measured on 1,488 channels of 186
        // clean DX/P migration pairs: buckets of six, symmetric in sign.
        int mapped = a <= 6 ? 0
            : a <= 12 ? 20
            : a <= 18 ? 32
            : a <= 24 ? 47
            : a <= 30 ? 57
            : 62;
        return sign * mapped;
    }

    private sealed class Converter
    {
        private readonly byte[] source;
        private readonly uint titleId;
        private readonly bool confirmedGuitarAliases;
        private readonly BdxPianoChordEncoding pianoChordEncoding;
        private readonly bool normaliseForP;
        private readonly List<string> warnings = [];
        private readonly byte[] gak = new byte[Gak.BinarySize];
        private readonly string title;
        private readonly string author;

        public Converter(
            byte[] source,
            uint titleId,
            string? authorOverride,
            string? defaultAuthor,
            bool confirmedGuitarAliases,
            BdxPianoChordEncoding pianoChordEncoding,
            bool normaliseForP)
        {
            this.source = source;
            this.titleId = titleId;
            this.confirmedGuitarAliases = confirmedGuitarAliases;
            this.pianoChordEncoding = pianoChordEncoding;
            this.normaliseForP = normaliseForP;

            var titleBuilder = new StringBuilder();
            for (int i = 0; i < 3; i++)
                titleBuilder.Append(DecodeBdxString(source.AsSpan(BdxLabels + 32 * i, 32)));
            title = titleBuilder.ToString().Replace("\n", string.Empty);

            string contributor = DecodeBdxString(source.AsSpan(BdxContributor, 32));
            author = !string.IsNullOrWhiteSpace(authorOverride) ? authorOverride : (string.IsNullOrEmpty(contributor) ? DefaultAuthorFor(defaultAuthor) : contributor);
        }

        public BdxConversionResult Convert()
        {
            DoHeader();
            DoChannels();
            DoKeyBlocks();
            DoEditorDefaults();

            var plays = Enumerable.Range(0, 8)
                .Select(c => source[BdxChannelInfo + 16 * c + 3])
                .ToArray();
            bool hasGuitar = plays.Contains(PlayGuitar);
            bool hasPiano = plays.Contains(PlayPiano);
            if (hasGuitar && hasPiano)
                throw new BdxConversionException("BDX contains both Guitar and Piano play types; shared DX chord data is ambiguous.");
            if (hasGuitar) DoGuitar();
            else if (hasPiano) DoPiano();

            DoKaraoke();
            var managerItem = MakeManagerItem(plays);
            var parsedGak = Gak.Read(gak);
            return new BdxConversionResult(parsedGak, managerItem, gak.ToArray(), warnings.AsReadOnly());
        }

        private void DoEditorDefaults()
        {
            gak.AsSpan(PPianoPair, 240).Fill(0xFF);

            int o = PGuitarTimer;
            WriteInt16(gak, o, 0);
            gak.AsSpan(o + 2, 20).Fill(0xFF);
            for (int k = 1; k < 32; k++)
            {
                int po = PGuitarTimer + 22 * k;
                WriteInt16(gak, po, k == 1 ? (short)-1 : (short)0);
                for (int j = 0; j < 10; j++)
                {
                    gak[po + 2 + 2 * j] = 0x0F;
                    gak[po + 3 + 2 * j] = 0x00;
                }
            }
        }

        private void DoHeader()
        {
            WriteUInt32(gak, PVersion, 0x00020001);
            WriteUInt32(gak, PTitleId, titleId);
            WriteUInt32(gak, PDateCreated, ReadUInt32(source, BdxDateCreated));
            WriteUInt32(gak, PDateModified, ReadUInt32(source, BdxDateModified));
            WriteUtf16Fixed(gak.AsSpan(PTitle, 500), title);

            gak[PUnknown204] = 1;
            gak[PUnknown204 + 1] = 0;
            gak[PUnknown204 + 2] = 0;

            byte melody = source[BdxMainInstrument];
            gak[PMelody] = melody == 0xFF ? (byte)0xFF : melody < 10 ? melody : (byte)0xFF;
            if (melody >= 8 && melody != 0xFF)
                warnings.Add($"unusual BDX melody channel {melody}; P melody set to -1");

            gak[PLinesPlus6] = source[BdxLinesPlus6];
            gak[PTimeSignature] = source[BdxTimeSignature];
            // Nintendo's migrator: P master = min(32, 16 + DX master), exact for
            // all 38 clean non-karaoke DX/P pairs in the corpus. Karaoke songs it
            // also gave Vocaloid vocals and lowered further by a content-dependent
            // amount; this converter adds no vocals, so the vocal-free rule applies.
            gak[PMasterGain] = normaliseForP
                ? (byte)Math.Min(32, 16 + source[BdxMasterVolume])
                : source[BdxMasterVolume];
            gak[PReserved20B] = 0;

            source.AsSpan(BdxTempo, 32 * 4).CopyTo(gak.AsSpan(PTempo));
        }

        private void DoChannels()
        {
            for (int c = 0; c < 8; c++)
            {
                int so = BdxChannelInfo + 16 * c;
                int po = PChannelInfo + 48 * c;
                short volume = ReadInt16(source, so);
                byte instrument = source[so + 2];
                byte play = source[so + 3];
                sbyte pan = unchecked((sbyte)source[so + 4]);
                byte masterPro = source[so + 5];
                byte clone = source[so + 6];
                byte amaBegi = source[so + 7];

                if (play > PlayPiano)
                    warnings.Add($"channel {c}: unknown play type {play}");

                byte pInstrument = confirmedGuitarAliases && play == PlayGuitar && instrument == 56
                    ? (byte)198
                    : instrument;

                gak[po] = play;
                gak[po + 1] = pInstrument;
                gak[po + 2] = (byte)Math.Clamp((int)volume, 0, 255);
                gak[po + 3] = clone;
                gak[po + 4] = masterPro;
                gak[po + 5] = amaBegi;
                gak[po + 6] = 0;
                // Nintendo DX migrations use 8 as the neutral/default value for
                // the P-only +07/+08 performance pair. Drum +08 is always 0.
                gak[po + 7] = 8;
                gak[po + 8] = play == PlayDrum ? (byte)0 : (byte)8;

                var env = source.AsSpan(BdxEnvelopes + 12 * c, 12);
                env.CopyTo(gak.AsSpan(po + 16, 12));
                env.CopyTo(gak.AsSpan(po + 32, 12));

                source.AsSpan(BdxNotes + 2048 * c, 2048)
                    .CopyTo(gak.AsSpan(PNotes + 2400 * c, 2048));

                for (int j = 0; j < 600; j++)
                    WriteInt16(gak, PNoteStrengths + c * 1200 + 2 * j, -1);

                int changeBase = BdxChanges + c * 32 * 8;
                for (int j = 0; j < 32; j++)
                {
                    int o = changeBase + j * 8;
                    short time = ReadInt16(source, o);
                    short range = ReadInt16(source, o + 2);
                    byte volumeChange = source[o + 4];
                    WriteTimeValuePair(gak, PVolume + c * 256 * 4 + j * 4, time, volumeChange);
                    WriteTimeValuePair(gak, PRange + c * 128 * 4 + j * 4, time, range);
                }

                WriteTimeValuePair(gak, PEffect + c * 128 * 4, 0, 0);
                WriteTimeValuePair(gak, PEffect + c * 128 * 4 + 4, -1, 0);
                WritePanTuple(PPan + 8 * c, normaliseForP ? MapPan(pan) : MapPanLegacy(pan));
            }

            foreach (int c in new[] { 8, 9 })
            {
                WriteTimeValuePair(gak, PVolume + c * 256 * 4, -1, 0);
                WriteTimeValuePair(gak, PRange + c * 128 * 4, -1, 0);
                WriteTimeValuePair(gak, PEffect + c * 128 * 4, 0, 0);
                WriteTimeValuePair(gak, PEffect + c * 128 * 4 + 4, -1, 0);
                WritePanTuple(PPan + 8 * c, 0);
            }
        }

        private void WritePanTuple(int offset, int pan)
        {
            WriteInt16(gak, offset, 0);
            WriteInt16(gak, offset + 2, (short)pan);
            WriteInt16(gak, offset + 4, -1);
            WriteInt16(gak, offset + 6, 0);
        }

        private void DoKeyBlocks()
        {
            int lines = Math.Max(1, source[BdxLinesPlus6] - 6);
            int beatsPerMeasure = source[BdxTimeSignature] is 3 or 4 ? source[BdxTimeSignature] : 4;
            int lastUsed = Math.Min(511, lines * beatsPerMeasure - 1);

            void Expand(int srcOffset, int dstOffset)
            {
                source.AsSpan(srcOffset, 512).CopyTo(gak.AsSpan(dstOffset, 512));
                gak[dstOffset] |= 0x80;
                byte finalState = (byte)(gak[dstOffset + lastUsed] & 0x7F);
                gak.AsSpan(dstOffset + 512, 88).Fill(finalState);
            }

            for (int c = 0; c < 8; c++)
                Expand(BdxKeyBlocks + c * 512, PKeyBlocks + c * 600);

            gak[PKeyBlocks + 8 * 600] = 0x80;
            gak[PKeyBlocks + 9 * 600] = 0x80;
            Expand(BdxKeyBlocks + 8 * 512, PKeyBlocks + 10 * 600);
        }

        private void DoGuitar()
        {
            for (int i = 0; i < 16; i++)
                WriteUInt32(gak, PGuitarOrig + 4 * i, ReadUInt32(source, BdxGuitarOrig + 4 * i) & 0x3FFFFFFF);

            bool sawEnd = false;
            for (int k = 0; k < 32; k++)
            {
                int so = BdxGuitarTimer + 24 * k;
                int po = PGuitarTimer + 22 * k;
                short time = ReadInt16(source, so);
                WriteInt16(gak, po, time);
                for (int j = 0; j < 10; j++)
                {
                    var pair = GuitarPairToP(source[so + 2 + 2 * j], source[so + 3 + 2 * j]);
                    gak[po + 2 + 2 * j] = pair.Index;
                    gak[po + 3 + 2 * j] = pair.Root;
                }
                if (time < 0)
                {
                    sawEnd = true;
                    break;
                }
            }
            if (!sawEnd)
                WriteInt16(gak, PGuitarTimer + 22 * 32, -1);

            int count = (int)Math.Min(ReadUInt32(source, BdxChordCount), 255u);
            count = Math.Min(count, 300);
            WriteUInt32(gak, PGuitarChordCount, (uint)count);
            for (int i = 0; i < count; i++)
            {
                int so = BdxChordTable + 4 * i;
                short time = ReadInt16(source, so);
                var pair = GuitarPairToP(source[so + 2], source[so + 3]);
                WriteTimeValuePair(gak, PGuitarChordTable + 4 * i, time, PairShort(pair.Index, pair.Root));
            }

            DoGuitarNoteMetadata();
        }

        private void DoGuitarNoteMetadata()
        {
            var palettes = new List<GuitarPalette>();
            for (int k = 0; k < 32; k++)
            {
                int so = BdxGuitarTimer + 24 * k;
                short time = ReadInt16(source, so);
                if (time < 0) break;

                var playable = new ChordPair[8];
                for (int j = 0; j < playable.Length; j++)
                {
                    int slot = BbpChordCatalog.GuitarPlayableSlots[j];
                    playable[j] = new ChordPair(source[so + 2 + 2 * slot], source[so + 3 + 2 * slot]);
                }
                palettes.Add(new GuitarPalette(time, playable));
            }
            if (palettes.Count == 0) return;

            int guitarChannel = -1;
            for (int c = 0; c < 8; c++)
            {
                if (source[BdxChannelInfo + 16 * c + 3] == PlayGuitar)
                {
                    guitarChannel = c;
                    break;
                }
            }
            if (guitarChannel < 0) return;

            var notes = source.AsSpan(BdxNotes + 2048 * guitarChannel, 2048);
            int paletteIndex = 0;
            for (int pos = 0; pos < notes.Length; pos++)
            {
                byte note = notes[pos];
                int cellStart = (pos / 4) * 4;
                int inCell = pos & 3;
                bool triplet = notes[cellStart] == 0xFF;
                int tick;
                if (triplet)
                {
                    if (inCell == 0)
                    {
                        WriteUInt16(gak, PGuitarNoteMetadata + 2 * pos, 0x00FF);
                        continue;
                    }
                    tick = (pos / 4) * 12 + (inCell - 1) * 4;
                }
                else
                {
                    tick = (pos / 4) * 12 + inCell * 3;
                }

                if (note == 0 || note >= 0x80) continue;
                int baseCode = note & 0x7F;
                int downCode = baseCode & ~0x02;
                int button = ButtonForDownCode(downCode);
                if (button < 0) continue;

                while (paletteIndex + 1 < palettes.Count && palettes[paletteIndex + 1].Time <= tick)
                    paletteIndex++;
                ChordPair pair = palettes[paletteIndex].Pairs[button];

                int muted = 0;
                if (pair.Index != 0xFF)
                {
                    if (pair.Root == 0xFF)
                    {
                        if (pair.Index < 16)
                            muted = MutedStringMaskFromWord(ReadUInt32(source, BdxGuitarOrig + 4 * pair.Index));
                        else
                            warnings.Add($"custom Guitar chord index {pair.Index} outside DX 0..15");
                    }
                    else
                    {
                        int root = BdxPairRoot(pair.Root);
                        if (root is >= 0 and < 12 && pair.Index < 13)
                            muted = MutedStringMaskFromWord(ReadUInt32(BbpChordCatalog.GuitarChords, 52 * root + 4 * pair.Index));
                        else
                            warnings.Add($"unknown Guitar chord 0x{pair.Index:X2}/0x{pair.Root:X2} at tick {tick}");
                    }
                }

                ushort value = (ushort)(0x8000 | ((baseCode & 0x02) != 0 ? 0x4000 : 0) | muted);
                WriteUInt16(gak, PGuitarNoteMetadata + 2 * pos, value);
            }
        }

        private void DoPiano()
        {
            if (pianoChordEncoding == BdxPianoChordEncoding.LegacyCompatibility)
            {
                DoPianoLegacyCompatibility();
                return;
            }

            DoPianoExact();
        }

        /// <summary>
        /// DX-ROM-grounded Piano conversion. Built-in and custom chords are
        /// first resolved exactly as AXBJ's ARM9 does. Native P descriptors are
        /// used only when the P ELF-backed generator reproduces that exact pitch
        /// multiset for the same quality; everything else remains literal.
        /// </summary>
        private void DoPianoExact()
        {
            var sourcePairs = ReadPianoPairs(out int maxCustom);
            int nextIndex = maxCustom + 1;
            if (nextIndex > 64)
                throw new BdxConversionException($"piano custom-chord index starts beyond P capacity: {nextIndex}");

            byte highestRaw = source[BdxPianoHighest];
            byte voicingRaw = source[BdxPianoVoicing];
            int mode = BdxPianoChordCatalog.NormalizeVoicing(voicingRaw);
            int voiceCount = BdxPianoChordCatalog.VoiceCount(voicingRaw);
            uint customFlags = ReadUInt32(source, BdxPianoCustomFlags);

            if (voicingRaw > 5)
                warnings.Add($"DX Piano voicing byte 0x{voicingRaw:X2} is outside 0..5; DX itself falls back to mode 0.");
            if (highestRaw is > 0 and < 25)
                warnings.Add($"DX Piano highest-note byte {highestRaw} is below 25; DX itself clamps it to 25.");
            else if (highestRaw >= 127)
                warnings.Add($"DX Piano highest-note byte {highestRaw} is >=127; DX itself clamps it to 127.");

            // Preserve all referenced source custom slots, but transform flagged
            // originals exactly as DX 0x0204F8F8 does. Unflagged originals are
            // literal pitches; keeping their source byte order is harmless and
            // friendlier for later Studio editing.
            int copyCount = Math.Min(nextIndex, 32);
            if (copyCount > 0)
                source.AsSpan(BdxPianoOrig, 4 * copyCount).CopyTo(gak.AsSpan(PPianoOrig, 4 * copyCount));

            int transformedCustomCount = 0;
            for (int customIndex = 0; customIndex < copyCount; customIndex++)
            {
                if ((customFlags & (1u << customIndex)) == 0)
                    continue;

                byte[] resolved = BdxPianoChordCatalog.ResolveCustom(
                    source.AsSpan(BdxPianoOrig + 4 * customIndex, 4),
                    true,
                    highestRaw,
                    voicingRaw);
                resolved.CopyTo(gak, PPianoOrig + 4 * customIndex);
                transformedCustomCount++;
            }

            var remap = new Dictionary<short, ChordPair>();
            int nativeCount = 0;
            int literalCount = 0;

            for (int i = 0; i < sourcePairs.Length; i++)
            {
                ChordPair pair = sourcePairs[i];
                if (pair.Root == 0xFF)
                {
                    if (pair.Index == 0xFF) break;
                    if (pair.Index >= 32)
                        throw new BdxConversionException($"custom Piano chord index {pair.Index} outside DX 0..31");
                    gak[PPianoPair + 2 * i] = pair.Index;
                    gak[PPianoPair + 2 * i + 1] = pair.Root;
                    continue;
                }

                short key = PairShort(pair.Index, pair.Root);
                if (remap.TryGetValue(key, out ChordPair existing))
                {
                    gak[PPianoPair + 2 * i] = existing.Index;
                    gak[PPianoPair + 2 * i + 1] = existing.Root;
                    continue;
                }

                if (!BdxPianoChordCatalog.TryResolveBuiltIn(
                        pair.Index, pair.Root, highestRaw, voicingRaw, out byte[] dxNotes))
                {
                    throw new BdxConversionException(
                        $"piano built-in chord 0x{pair.Index:X2}/0x{pair.Root:X2} is outside the DX ROM table/root encoding");
                }

                ChordPair outputPair;

                if (pianoChordEncoding == BdxPianoChordEncoding.NativeWhenExact
                    && TryMapExactDxPianoChordToNativeP(pair, dxNotes, voiceCount, out outputPair))
                {
                    nativeCount++;
                }
                else
                {
                    outputPair = MaterializeExactPianoChord(dxNotes, ref nextIndex);
                    literalCount++;
                }

                gak[PPianoPair + 2 * i] = outputPair.Index;
                gak[PPianoPair + 2 * i + 1] = outputPair.Root;
                remap[key] = outputPair;
            }

            RewritePianoChordEvents(remap);

            if (transformedCustomCount > 0)
            {
                warnings.Add(
                    $"DX Piano custom-voicing mask 0x{customFlags:X8}: applied DX global voicing to {transformedCustomCount} referenced custom chord(s).");
            }
            if (pianoChordEncoding == BdxPianoChordEncoding.NativeWhenExact && literalCount > 0)
            {
                string spread = (mode % 3) switch { 0 => "close", 1 => "medium", _ => "wide" };
                warnings.Add(
                    $"Piano: {nativeCount} built-in chord(s) mapped to native P descriptors; {literalCount} required exact literal fallback " +
                    $"(DX mode {mode}: {voiceCount} voices/{spread}).");
            }
        }

        private ChordPair[] ReadPianoPairs(out int maxCustom)
        {
            var sourcePairs = new ChordPair[32];
            maxCustom = -1;
            for (int i = 0; i < sourcePairs.Length; i++)
            {
                var pair = new ChordPair(source[BdxPianoPair + 2 * i], source[BdxPianoPair + 2 * i + 1]);
                sourcePairs[i] = pair;
                if (pair.Root == 0xFF && pair.Index != 0xFF)
                    maxCustom = Math.Max(maxCustom, pair.Index);
            }
            return sourcePairs;
        }

        private bool TryMapExactDxPianoChordToNativeP(
            ChordPair dxPair,
            ReadOnlySpan<byte> dxNotes,
            int voiceCount,
            out ChordPair pPair)
        {
            pPair = default;

            // DX has a thirteenth built-in quality (m6). Native P only has 12,
            // so quality 12 is necessarily a literal fallback.
            if (dxPair.Index >= 12
                || !BdxPianoChordCatalog.TryDecodeRootDescriptor(dxPair.Root, out int degree, out int accidental))
            {
                return false;
            }

            // Preserve the chord's semantic quality/root. P's richer per-chord
            // voicing fields are searched exhaustively over the ordinary editor
            // rotation range and both drop states; exact pitch equality decides.
            for (int drop = 0; drop <= 1; drop++)
            {
                byte rawRoot = unchecked((byte)(
                    (degree << 5)
                    | 0x10
                    | (voiceCount == 4 ? 0x08 : 0)
                    | (accidental << 1)
                    | drop));

                for (int rotation = 1; rotation <= 13; rotation++)
                {
                    var candidate = new ChordPair(
                        unchecked((byte)((dxPair.Index << 4) | rotation)),
                        rawRoot);
                    if (!BbpPianoChordCatalog.TryResolve(
                            new IndexRootPair { index = candidate.Index, rawRoot = candidate.Root },
                            Array.Empty<int>(),
                            out byte[] generated))
                    {
                        continue;
                    }

                    if (SamePitchMultiset(dxNotes, generated))
                    {
                        pPair = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private ChordPair MaterializeExactPianoChord(ReadOnlySpan<byte> notes, ref int nextIndex)
        {
            if (nextIndex >= 64)
                throw new BdxConversionException("piano conversion requires more than 64 P original chords");

            notes[..Math.Min(4, notes.Length)].CopyTo(gak.AsSpan(PPianoOrig + 4 * nextIndex, 4));
            var result = new ChordPair((byte)nextIndex, 0xFF);
            nextIndex++;
            return result;
        }

        private void RewritePianoChordEvents(IReadOnlyDictionary<short, ChordPair> remap)
        {
            int count = (int)Math.Min(ReadUInt32(source, BdxChordCount), 255u);
            count = Math.Min(count, 300);
            WriteUInt32(gak, PPianoChordCount, (uint)count);
            for (int i = 0; i < count; i++)
            {
                int so = BdxChordTable + 4 * i;
                short time = ReadInt16(source, so);
                var pair = new ChordPair(source[so + 2], source[so + 3]);
                if (pair.Root != 0xFF)
                {
                    short key = PairShort(pair.Index, pair.Root);
                    if (!remap.TryGetValue(key, out pair))
                        throw new BdxConversionException(
                            $"piano chord event references unmapped pair 0x{source[so + 2]:X2}/0x{source[so + 3]:X2}");
                }
                WriteTimeValuePair(gak, PPianoChordTable + 4 * i, time, PairShort(pair.Index, pair.Root));
            }
        }

        private static bool SamePitchMultiset(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            byte[] a = left.ToArray().Where(static value => value != 0).ToArray();
            byte[] b = right.ToArray().Where(static value => value != 0).ToArray();
            if (a.Length != b.Length)
                return false;
            Array.Sort(a);
            Array.Sort(b);
            return a.AsSpan().SequenceEqual(b);
        }

        /// <summary>
        /// Historical pre-ROM implementation retained solely so the 51-file
        /// Python archaeology manifest can remain a byte-for-byte regression
        /// oracle. It deliberately ignores DX spread and the custom flag mask,
        /// and its 3-voice four-tone handling is not DX-exact.
        /// </summary>
        private void DoPianoLegacyCompatibility()
        {
            var sourcePairs = new ChordPair[32];
            int maxCustom = -1;
            for (int i = 0; i < sourcePairs.Length; i++)
            {
                var pair = new ChordPair(source[BdxPianoPair + 2 * i], source[BdxPianoPair + 2 * i + 1]);
                sourcePairs[i] = pair;
                if (pair.Root == 0xFF && pair.Index != 0xFF)
                    maxCustom = Math.Max(maxCustom, pair.Index);
            }

            int nextIndex = maxCustom + 1;
            if (nextIndex > 64)
                throw new BdxConversionException($"piano custom-chord index starts beyond P capacity: {nextIndex}");

            int copyCount = Math.Min(nextIndex, 32);
            if (copyCount > 0)
                source.AsSpan(BdxPianoOrig, 4 * copyCount).CopyTo(gak.AsSpan(PPianoOrig, 4 * copyCount));

            int minimum = 3 + source[BdxPianoVoicing] / 3;
            int highest = source[BdxPianoHighest] == 0 ? 70 : source[BdxPianoHighest];
            var remap = new Dictionary<short, ChordPair>();

            for (int i = 0; i < sourcePairs.Length; i++)
            {
                ChordPair pair = sourcePairs[i];
                if (pair.Root == 0xFF)
                {
                    if (pair.Index == 0xFF) break;
                    gak[PPianoPair + 2 * i] = pair.Index;
                    gak[PPianoPair + 2 * i + 1] = pair.Root;
                    continue;
                }

                if (nextIndex >= 64)
                    throw new BdxConversionException("piano conversion requires more than 64 P original chords");
                if (pair.Index >= BdxPianoChordCatalog.BaseChords.Length / 4)
                    throw new BdxConversionException($"piano built-in chord index {pair.Index} outside known DX table");

                gak[PPianoPair + 2 * i] = (byte)nextIndex;
                gak[PPianoPair + 2 * i + 1] = 0xFF;

                int root = BdxPairRoot(pair.Root);
                var notes = new byte[4];
                for (int j = 0; j < 4; j++)
                {
                    int baseNote = BdxPianoChordCatalog.BaseChords[4 * pair.Index + j];
                    int note;
                    if (baseNote == 0)
                    {
                        note = 0;
                    }
                    else
                    {
                        int pitchClass = (baseNote + root) % 12;
                        note = (pitchClass - highest) % 12 + highest;
                    }
                    notes[j] = unchecked((byte)note);
                }
                Array.Sort(notes);
                Array.Reverse(notes);
                if (notes[3] == 0 && minimum == 4)
                    notes[3] = unchecked((byte)(notes[0] - 12));
                notes.CopyTo(gak, PPianoOrig + 4 * nextIndex);

                remap[PairShort(pair.Index, pair.Root)] = new ChordPair((byte)nextIndex, 0xFF);
                nextIndex++;
            }

            RewritePianoChordEvents(remap);
        }

        private void DoKaraoke()
        {
            gak.AsSpan(PKaraokeTimer, 6000).Clear();
            WriteUInt16(gak, PKaraokeTimer + 2 * 2999, 0x3FFF);

            if (source[BdxHasKaraoke] == 0)
            {
                WriteUInt16(gak, PKaraokeTimer, 0x3FFF);
                return;
            }

            for (int i = 0; i < 2048; i++)
                WriteUInt16(gak, PKaraokeTimer + 2 * i, 0x3FFF);

            string decoded = DecodeBdxString(source.AsSpan(BdxKaraokeLyrics, 2048), lyricsMode: true);
            // Full-width forms are a fixed 14.4 px in cbf_std against 3.6-13.8 px for
            // ASCII, so English lyrics stay half-width (like the scorefixes) and
            // only Japanese lyrics get Nintendo's full-width forms.
            bool fullWidth = normaliseForP && !MostlyLatin(decoded);
            var chars = decoded.ToList();
            while (chars.Count < 2048) chars.Add(ZeroWidthSpace);

            var outputChars = new List<char>();
            var outputTimes = new List<ushort>();
            for (int i = 0; i < 2048; i++)
            {
                char ch = chars[i];
                if (ch == ZeroWidthSpace) continue;
                ushort rawTimer = ReadUInt16(source, BdxKaraokeTimer + 2 * i);
                if ((rawTimer & 0x3FFF) == 0x3FFF) break;

                ushort timer = (ushort)(rawTimer | 0x8000);
                if (normaliseForP)
                {
                    // Like Nintendo's migrator and P's own karaoke normaliser:
                    // full-width text, and separators flagged 0xC000.
                    if (fullWidth) ch = ToFullWidth(ch);
                    // Only whitespace is a separator. DX flags symbols (… ☆ ♪ × →) as well, but none of the
                    // 5,967 official karaoke BBPs does, and the aligner needs an ordinary timer on every
                    // non-whitespace character (corpus-inferred).
                    if (ch is '\n' or ' ' or '\u3000') timer |= 0x4000;
                    else timer &= 0xBFFF;
                    // DX shows three lines per page and pads stanzas out to a page
                    // with blank lines. P scrolls, so one blank line is enough.
                    if (ch == '\n' && outputChars.Count >= 2 && outputChars[^1] == '\n' && outputChars[^2] == '\n')
                        continue;
                }
                outputChars.Add(ch);
                outputTimes.Add(timer);
                if (outputChars.Count >= 2999)
                {
                    warnings.Add("karaoke truncated to P's 3000-code-unit field");
                    break;
                }
            }

            if (normaliseForP && outputChars.Count is > 0 and < 2999 && outputChars[^1] != '\n')
            {
                // Nintendo always terminates migrated lyrics with a line break whose
                // timer is padding with the separator bit (0x3FFF | 0x4000).
                outputChars.Add('\n');
                outputTimes.Add(0x7FFF);
            }

            WriteUtf16Fixed(gak.AsSpan(PKaraokeLyrics, 6000), new string(outputChars.ToArray()));
            for (int i = 0; i < outputTimes.Count; i++)
                WriteUInt16(gak, PKaraokeTimer + 2 * i, outputTimes[i]);
            WriteUInt16(gak, PKaraokeTimer + 2 * outputTimes.Count, 0x3FFF);
        }

        private static bool MostlyLatin(string lyrics)
        {
            int latin = lyrics.Count(char.IsAsciiLetter);
            int other = lyrics.Count(c => char.IsLetter(c) && !char.IsAscii(c));
            return latin > other;
        }

        /// <summary>
        /// Nintendo's migrated lyrics use full-width forms for printable ASCII,
        /// except '?' (kept) and the apostrophe (U+2019); space becomes U+3000.
        /// </summary>
        private static char ToFullWidth(char ch) => ch switch
        {
            ' ' => '\u3000',
            '?' => '?',
            '\'' => '\u2019',
            >= '!' and <= '~' => (char)(ch + 0xFEE0),
            _ => ch
        };

        private JbMgr.Item MakeManagerItem(IReadOnlyCollection<byte> plays)
        {
            int parts = 0;
            for (int c = 0; c < 8; c++)
            {
                if (source[BdxChannelInfo + 16 * c + 2] != 0)
                    parts++;
            }

            int flags = 0;
            flags |= 1 << 0;
            flags |= 1 << 1;
            flags |= (parts & 0x1F) << 3;
            if (source[BdxMainInstrument] != 0xFF) flags |= 1 << 8;
            if (source[BdxHasKaraoke] != 0) flags |= 1 << 11;
            flags |= 1 << 12;
            if (plays.Contains(PlayGuitar)) flags |= 1 << 14;
            if (plays.Contains(PlayDrum)) flags |= 1 << 15;
            if (plays.Contains(PlayPiano)) flags |= 1 << 16;
            if (source[BdxTimeSignature] == 3) flags |= 1 << 17;

            var item = JbMgr.Item.CreateEmpty();
            item.titleID = titleId;
            item.otherID = 0;
            item.flags = new JbMgr.Item.JbFlags { flag = flags };
            item.singer = JbMgr.Item.JbSinger.None;
            item.icon = JbMgr.Item.JbIcon.None;
            item.title = title;
            item.titleSimple = JbMgr.Item.SimpleTitle(title);
            item.author = author;
            item.videoID = 0;
            item.scores = new byte[50];
            item.zeroPadding = 0;
            return item;
        }
    }

    private readonly record struct ChordPair(byte Index, byte Root);
    private sealed record GuitarPalette(short Time, ChordPair[] Pairs);

    private static ChordPair GuitarPairToP(byte index, byte rawRoot)
    {
        if (index == 0xFF && rawRoot == 0xFF) return new ChordPair(0xFF, 0xFF);
        if (rawRoot == 0xFF) return new ChordPair(index, 0xEF);
        return new ChordPair(unchecked((byte)(index * 16 + 15)), RotateLeft8(rawRoot, 5));
    }

    private static byte RotateLeft8(byte value, int count)
    {
        count &= 7;
        if (count == 0) return value;
        return unchecked((byte)((value << count) | (value >> (8 - count))));
    }

    private static short PairShort(byte index, byte rawRoot)
        => unchecked((short)((rawRoot << 8) | index));

    private static int BdxPairRoot(byte rawRoot)
    {
        if (rawRoot == 0xFF) return 0xFF;
        int degree = rawRoot & 0x0F;
        if (degree >= MajorScale.Length) return degree % 12;
        int note = MajorScale[degree];
        int accidental = rawRoot >> 4;
        if (accidental == 1) note++;
        else if (accidental == 2) note--;
        return note & 0xFF;
    }

    private static int MutedStringMaskFromWord(uint value)
    {
        int mask = 0;
        for (int s = 0; s < 6; s++)
        {
            if (((value >> (5 * s)) & 31) >= 16)
                mask |= 1 << s;
        }
        return mask == 0x3F ? 0 : mask;
    }

    private static int ButtonForDownCode(int code) => code switch
    {
        0x05 => 0,
        0x09 => 1,
        0x0D => 2,
        0x11 => 3,
        0x19 => 4,
        0x1D => 5,
        0x21 => 6,
        0x25 => 7,
        _ => -1
    };

    private static string DecodeBdxString(ReadOnlySpan<byte> bytes, bool lyricsMode = false)
        => Language.GetBDXJPString(bytes, lyricsMode);

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset)
        => EndianBinaryPrimitives.ReadInt16(data.Slice(offset, 2), Endianness.LittleEndian);

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
        => EndianBinaryPrimitives.ReadUInt16(data.Slice(offset, 2), Endianness.LittleEndian);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        => EndianBinaryPrimitives.ReadUInt32(data.Slice(offset, 4), Endianness.LittleEndian);

    private static void WriteInt16(Span<byte> data, int offset, short value)
        => EndianBinaryPrimitives.WriteInt16(data.Slice(offset, 2), value, Endianness.LittleEndian);

    private static void WriteUInt16(Span<byte> data, int offset, ushort value)
        => EndianBinaryPrimitives.WriteUInt16(data.Slice(offset, 2), value, Endianness.LittleEndian);

    private static void WriteUInt32(Span<byte> data, int offset, uint value)
        => EndianBinaryPrimitives.WriteUInt32(data.Slice(offset, 4), value, Endianness.LittleEndian);

    private static void WriteTimeValuePair(Span<byte> data, int offset, short time, int value)
    {
        WriteInt16(data, offset, time);
        WriteInt16(data, offset + 2, unchecked((short)value));
    }

    internal static void WriteUtf16Fixed(Span<byte> destination, string? text)
    {
        destination.Clear();
        text ??= string.Empty;

        // These fields originated as ByValTStr buffers. Keep the final UTF-16
        // code unit clear for the terminator so raw BDX conversion and the
        // EndianBinaryIO GAK serializer have identical boundary semantics.
        int capacity = destination.Length / 2;
        int maxCodeUnits = Math.Max(0, capacity - 1);
        int codeUnits = Math.Min(text.Length, maxCodeUnits);
        if (codeUnits > 0 && codeUnits < text.Length && char.IsHighSurrogate(text[codeUnits - 1]))
            codeUnits--;
        Encoding.Unicode.GetBytes(text.AsSpan(0, codeUnits), destination);
    }
}
