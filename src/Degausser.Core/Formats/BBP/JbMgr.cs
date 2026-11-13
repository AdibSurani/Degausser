using System.Diagnostics;
using Kermalis.EndianBinaryIO;

namespace Degausser;

/// <summary>
/// Main Band Brothers P song catalogue manager. The on-disk format is a fixed
/// little-endian header followed by exactly 3700 312-byte items.
/// </summary>
public class JbMgr
{
    public const int HeaderSize = 8;
    public const int ItemCount = 3700;
    public const int BinarySize = HeaderSize + (Item.ItemBinarySize * ItemCount);

    private sealed class DiskManager
    {
        public DiskManager() { }
        public int Magic { get; set; }
        public short Version { get; set; }
        public short Count { get; set; }

        [BinaryArrayFixedLength(ItemCount)]
        public Item.DiskItem[] Items { get; set; } =
            Enumerable.Range(0, ItemCount).Select(_ => new Item.DiskItem()).ToArray();
    }

    public int magic;
    public short version;
    public short count;
    public Item[] items = [];

    public static JbMgr Empty => CreateEmpty();

    public static JbMgr CreateEmpty()
        => new()
        {
            magic = 0x4A4B4258, // "JKBX"
            version = 0x101,
            count = ItemCount,
            items = Enumerable.Range(0, ItemCount)
                .Select(_ => Item.CreateEmpty())
                .ToArray()
        };

    public static JbMgr Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BinarySize)
            throw new ArgumentException($"JbMgr requires {BinarySize} bytes.", nameof(bytes));

        using var stream = new MemoryStream(bytes[..BinarySize].ToArray(), writable: false);
        var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
        DiskManager disk = reader.ReadObject<DiskManager>();

        if (stream.Position != BinarySize)
            throw new InvalidDataException($"JbMgr parser consumed {stream.Position} bytes; expected {BinarySize}.");

        return new JbMgr
        {
            magic = disk.Magic,
            version = disk.Version,
            count = disk.Count,
            items = disk.Items.Select(Item.FromDisk).ToArray()
        };
    }

    public byte[] ToBytes()
    {
        var disk = new DiskManager
        {
            Magic = magic,
            Version = version,
            Count = count,
            Items = Enumerable.Range(0, ItemCount)
                .Select(i => (i < items.Length ? items[i] : Item.Empty).ToDisk())
                .ToArray()
        };

        using var stream = new MemoryStream(BinarySize);
        var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
        writer.WriteObject(disk);

        byte[] result = stream.ToArray();
        if (result.Length != BinarySize)
            throw new InvalidDataException($"JbMgr serialized to {result.Length} bytes; expected {BinarySize}.");
        return result;
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length < BinarySize)
            throw new ArgumentException($"JbMgr requires {BinarySize} bytes.", nameof(destination));
        ToBytes().AsSpan().CopyTo(destination);
    }

    [DebuggerDisplay("{titleID==(uint)-1?\"(empty)\":title,nq}")]
    public struct Item
    {
        public const int ItemBinarySize = 312;

        // EndianBinaryIO serializes public properties in declaration order.
        // The string lengths mirror the old ByValTStr SizeConst values.
        internal sealed class DiskItem
        {
            public DiskItem() { }

            public uint TitleID { get; set; }
            public uint OtherID { get; set; }
            public int Flags { get; set; }
            public short Singer { get; set; }
            public short Icon { get; set; }

            [BinaryStringFixedLength(51)]
            [BinaryStringTrimNullTerminators]
            public string Title { get; set; } = string.Empty;

            [BinaryStringFixedLength(51)]
            [BinaryStringTrimNullTerminators]
            public string TitleSimple { get; set; } = string.Empty;

            [BinaryStringFixedLength(18)]
            [BinaryStringTrimNullTerminators]
            public string Author { get; set; } = string.Empty;

            public uint VideoID { get; set; }

            [BinaryArrayFixedLength(50)]
            public byte[] Scores { get; set; } = new byte[50];

            public short ZeroPadding { get; set; }
        }

        public uint titleID;
        public uint otherID;
        public JbFlags flags;
        public JbSinger singer;
        public JbIcon icon;
        public string title;
        public string titleSimple;
        public string author;
        public uint videoID;
        public byte[] scores; // Beginner, Amateur, Pro, Master, Vocals
        public short zeroPadding;

        public static Item Empty => CreateEmpty();

        /// <summary>
        /// The <see cref="titleSimple"/> for a title Degausser writes: one line, full-width Latin folded to
        /// ASCII, and lowercase. The game files songs under a letter by this field. Across the 9,722 official
        /// BBPs its first character is katakana (7,170), lowercase ASCII (2,455), a digit (86) or another
        /// symbol (11), and never uppercase ASCII; a title_simple that starts with a capital is filed under
        /// symbols (hardware-observed). Nintendo also writes kana readings as katakana without dakuten; that
        /// isn't attempted here, so kana and kanji are kept as they are (corpus-inferred).
        /// </summary>
        public static string SimpleTitle(string? title)
        {
            var simple = new System.Text.StringBuilder((title ?? string.Empty).Length);
            foreach (char c in title ?? string.Empty)
            {
                if (c is '\r' or '\n') continue;
                char ascii = c == '\u3000' ? ' ' : c is >= '\uFF01' and <= '\uFF5E' ? (char)(c - 0xFEE0) : c;
                simple.Append(char.ToLowerInvariant(ascii));
            }
            return simple.ToString();
        }

        public static Item CreateEmpty()
            => new()
            {
                titleID = 0xFFFFFFFF,
                singer = JbSinger.None,
                title = string.Empty,
                titleSimple = string.Empty,
                author = string.Empty,
                scores = new byte[50]
            };

        public static Item Read(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < ItemBinarySize)
                throw new ArgumentException($"Item requires {ItemBinarySize} bytes.", nameof(bytes));

            using var stream = new MemoryStream(bytes[..ItemBinarySize].ToArray(), writable: false);
            var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
            return FromDisk(reader.ReadObject<DiskItem>());
        }

        internal static Item FromDisk(DiskItem raw)
            => new()
            {
                titleID = raw.TitleID,
                otherID = raw.OtherID,
                flags = new JbFlags { flag = raw.Flags },
                singer = (JbSinger)raw.Singer,
                icon = (JbIcon)raw.Icon,
                title = raw.Title,
                titleSimple = raw.TitleSimple,
                author = raw.Author,
                videoID = raw.VideoID,
                scores = NormalizeScores(raw.Scores),
                zeroPadding = raw.ZeroPadding
            };

        internal readonly DiskItem ToDisk()
            => new()
            {
                TitleID = titleID,
                OtherID = otherID,
                Flags = flags.flag,
                Singer = (short)singer,
                Icon = (short)icon,
                // The old MarshalAs(ByValTStr, SizeConst=N) representation
                // reserves one code unit for NUL. Preserve that behaviour.
                Title = NormalizeByValTStr(title, 51),
                TitleSimple = NormalizeByValTStr(titleSimple, 51),
                Author = NormalizeByValTStr(author, 18),
                VideoID = videoID,
                Scores = NormalizeScores(scores),
                ZeroPadding = zeroPadding
            };

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream(ItemBinarySize);
            var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
            writer.WriteObject(ToDisk());
            byte[] result = stream.ToArray();
            if (result.Length != ItemBinarySize)
                throw new InvalidDataException($"JbMgr.Item serialized to {result.Length} bytes; expected {ItemBinarySize}.");
            return result;
        }

        public void Write(Span<byte> destination)
        {
            if (destination.Length < ItemBinarySize)
                throw new ArgumentException($"Item requires {ItemBinarySize} bytes.", nameof(destination));
            ToBytes().AsSpan().CopyTo(destination);
        }

        private static byte[] NormalizeScores(byte[]? source)
        {
            var result = new byte[50];
            if (source is not null)
                source.AsSpan(0, Math.Min(source.Length, result.Length)).CopyTo(result);
            return result;
        }

        private static string NormalizeByValTStr(string? source, int sizeConst)
        {
            string text = source ?? string.Empty;
            int nul = text.IndexOf('\0');
            if (nul >= 0) text = text[..nul];

            int maxChars = Math.Max(0, sizeConst - 1);
            if (text.Length <= maxChars) return text;

            int length = maxChars;
            if (length > 0 && char.IsHighSurrogate(text[length - 1]))
                length--;
            return text[..length];
        }

        public enum JbSinger : short
        {
            None = -1, OriginalMale = -2, OriginalFemale = -3
        }

        public enum JbIcon : short
        {
            None, Heart, Skull, Music, Warning
        }

        [DebuggerDisplay("{IsValid}")]
        public struct JbFlags
        {
            public int flag;

            // Bit0 = 1
            public bool OnSD { get { return this[1]; } set { this[1] = value; } } // confirmed/high: pack stored in SD extdata
            // Bit2 = 0
            // Bits 3..7: five-bit part count. Official content only needs <=10,
            // but the native layout really reserves all five bits.
            public int Parts { get => (flag >> 3) & 0x1F; set => flag = (flag & ~0xF8) | ((value & 0x1F) << 3); }
            public bool HasMelody { get { return this[8]; } set { this[8] = value; } }
            public bool IsSingable { get { return this[9]; } set { this[9] = value; } }
            public bool IsClassic { get { return this[10]; } set { this[10] = value; } }
            public bool HasLyrics { get { return this[11]; } set { this[11] = value; } }
            public bool IsReceived { get { return this[12]; } set { this[12] = value; } }
            public bool HasFans { get { return this[13]; } set { this[13] = value; } }
            public bool HasGuitar { get { return this[14]; } set { this[14] = value; } }
            public bool HasDrum { get { return this[15]; } set { this[15] = value; } }
            public bool HasPiano { get { return this[16]; } set { this[16] = value; } }
            public bool IsThreeFourMeter { get { return this[17]; } set { this[17] = value; } }
            public bool HasVocals { get { return this[20]; } set { this[20] = value; } }
            public bool IsOnlineable { get { return this[21]; } set { this[21] = value; } }
            public bool IsHidden { get { return this[23]; } set { this[23] = value; } }

            public bool this[int i]
            {
                get => (flag & (1 << i)) != 0;
                set => flag = (flag & ~(1 << i)) | ((value ? 1 : 0) << i);
            }

            public bool IsValid
            {
                // Exact legacy mask preserved; the historical prose comment did
                // not actually match it, so do not reinterpret without evidence.
                get => (flag & 0x4C0005) == 1 && IsSingable == HasVocals;
                set => flag = (flag & ~0x4C0005) | (value ? 1 : 0);
            }
        }
    }
}
