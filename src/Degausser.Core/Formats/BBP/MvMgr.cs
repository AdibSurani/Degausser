using Kermalis.EndianBinaryIO;

namespace Degausser;

/// <summary>1000-entry Band Brothers P movie metadata manager.</summary>
public class MvMgr
{
    public const int HeaderSize = 8;
    public const int ItemCount = 1000;
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

    public static MvMgr Empty => CreateEmpty();

    public static MvMgr CreateEmpty()
        => new()
        {
            magic = 0x4D564431, // "MVD1"
            version = 0x100,
            count = ItemCount,
            items = Enumerable.Range(0, ItemCount).Select(_ => Item.CreateEmpty()).ToArray()
        };

    public static MvMgr Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BinarySize)
            throw new ArgumentException($"MvMgr requires {BinarySize} bytes.", nameof(bytes));

        using var stream = new MemoryStream(bytes[..BinarySize].ToArray(), writable: false);
        var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
        DiskManager disk = reader.ReadObject<DiskManager>();

        if (stream.Position != BinarySize)
            throw new InvalidDataException($"MvMgr parser consumed {stream.Position} bytes; expected {BinarySize}.");

        return new MvMgr
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
            throw new InvalidDataException($"MvMgr serialized to {result.Length} bytes; expected {BinarySize}.");
        return result;
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length < BinarySize)
            throw new ArgumentException($"MvMgr requires {BinarySize} bytes.", nameof(destination));
        ToBytes().CopyTo(destination);
    }

    public struct Item
    {
        public const int ItemBinarySize = 392;

        internal sealed class DiskItem
        {
            public DiskItem() { }
            public uint HasItem { get; set; }
            public uint VideoID { get; set; }

            [BinaryStringFixedLength(51)]
            [BinaryStringTrimNullTerminators]
            public string Title { get; set; } = string.Empty;

            [BinaryStringFixedLength(141)]
            [BinaryStringTrimNullTerminators]
            public string Description { get; set; } = string.Empty;
        }

        public uint hasItem;
        public uint videoID;
        public string title;
        public string description;

        public static Item Empty => CreateEmpty();
        public static Item CreateEmpty() => new() { title = string.Empty, description = string.Empty };

        public static Item Read(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < ItemBinarySize)
                throw new ArgumentException($"MvMgr.Item requires {ItemBinarySize} bytes.", nameof(bytes));

            using var stream = new MemoryStream(bytes[..ItemBinarySize].ToArray(), writable: false);
            var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
            return FromDisk(reader.ReadObject<DiskItem>());
        }

        internal static Item FromDisk(DiskItem raw)
            => new()
            {
                hasItem = raw.HasItem,
                videoID = raw.VideoID,
                title = raw.Title,
                description = raw.Description
            };

        internal readonly DiskItem ToDisk()
            => new()
            {
                HasItem = hasItem,
                VideoID = videoID,
                // The old fixed char buffers intentionally reserved a NUL.
                Title = NormalizeFixedString(title, 51),
                Description = NormalizeFixedString(description, 141)
            };

        public readonly byte[] ToBytes()
        {
            using var stream = new MemoryStream(ItemBinarySize);
            var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
            writer.WriteObject(ToDisk());

            byte[] result = stream.ToArray();
            if (result.Length != ItemBinarySize)
                throw new InvalidDataException($"MvMgr.Item serialized to {result.Length} bytes; expected {ItemBinarySize}.");
            return result;
        }

        public readonly void Write(Span<byte> destination)
        {
            if (destination.Length < ItemBinarySize)
                throw new ArgumentException($"MvMgr.Item requires {ItemBinarySize} bytes.", nameof(destination));
            ToBytes().CopyTo(destination);
        }

        private static string NormalizeFixedString(string? source, int sizeConst)
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
    }
}
