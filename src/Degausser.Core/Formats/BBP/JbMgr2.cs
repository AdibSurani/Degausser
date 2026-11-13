using System.Diagnostics;
using Kermalis.EndianBinaryIO;

namespace Degausser;

/// <summary>
/// Secondary 1500-entry filtered Jb catalogue/cache. The 2026 ELF pass
/// recovered its real header magic and confirmed that it is a projection of
/// the main catalogue rather than another bank of ordinary song slots.
/// </summary>
public class JbMgr2
{
    public const int HeaderSize = 8;
    public const int ItemCount = 1500;
    public const int BinarySize = HeaderSize + (Item.ItemBinarySize * ItemCount);

    private sealed class DiskManager
    {
        public DiskManager() { }
        public int Magic { get; set; }
        public ushort Version { get; set; }
        public ushort Count { get; set; }

        [BinaryArrayFixedLength(ItemCount)]
        public Item.DiskItem[] Items { get; set; } =
            Enumerable.Range(0, ItemCount).Select(_ => new Item.DiskItem()).ToArray();
    }

    public int magic;
    public ushort version;
    public ushort count;
    public Item[] items = [];

    public static JbMgr2 Empty => CreateEmpty();

    public static JbMgr2 CreateEmpty()
        => new()
        {
            magic = 0x4A425632, // recovered mgr2.bin magic (bytes 32 56 42 4A)
            version = 0x101,
            count = ItemCount,
            items = Enumerable.Range(0, ItemCount).Select(_ => Item.CreateEmpty()).ToArray()
        };

    public static JbMgr2 Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BinarySize)
            throw new ArgumentException($"JbMgr2 requires {BinarySize} bytes.", nameof(bytes));

        using var stream = new MemoryStream(bytes[..BinarySize].ToArray(), writable: false);
        var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
        DiskManager disk = reader.ReadObject<DiskManager>();

        if (stream.Position != BinarySize)
            throw new InvalidDataException($"JbMgr2 parser consumed {stream.Position} bytes; expected {BinarySize}.");

        return new JbMgr2
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
            throw new InvalidDataException($"JbMgr2 serialized to {result.Length} bytes; expected {BinarySize}.");
        return result;
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length < BinarySize)
            throw new ArgumentException($"JbMgr2 requires {BinarySize} bytes.", nameof(destination));
        ToBytes().CopyTo(destination);
    }

    [DebuggerDisplay("{item.titleID==(uint)-1?\"(empty)\":item.title,nq}")]
    public struct Item
    {
        public const int ItemBinarySize = JbMgr.Item.ItemBinarySize + 8;

        internal sealed class DiskItem
        {
            public DiskItem() { }
            public JbMgr.Item.DiskItem Item { get; set; } = new();
            public uint Unk1 { get; set; }
            public uint Unk2 { get; set; }
        }

        public JbMgr.Item item;
        public uint unk1;
        public uint unk2;

        public static Item Empty => CreateEmpty();
        public static Item CreateEmpty() => new() { item = JbMgr.Item.CreateEmpty() };

        public static Item Read(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < ItemBinarySize)
                throw new ArgumentException($"JbMgr2.Item requires {ItemBinarySize} bytes.", nameof(bytes));

            using var stream = new MemoryStream(bytes[..ItemBinarySize].ToArray(), writable: false);
            var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
            return FromDisk(reader.ReadObject<DiskItem>());
        }

        internal static Item FromDisk(DiskItem raw)
            => new()
            {
                item = JbMgr.Item.FromDisk(raw.Item),
                unk1 = raw.Unk1,
                unk2 = raw.Unk2
            };

        internal readonly DiskItem ToDisk()
            => new()
            {
                Item = item.ToDisk(),
                Unk1 = unk1,
                Unk2 = unk2
            };

        public readonly byte[] ToBytes()
        {
            using var stream = new MemoryStream(ItemBinarySize);
            var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
            writer.WriteObject(ToDisk());

            byte[] result = stream.ToArray();
            if (result.Length != ItemBinarySize)
                throw new InvalidDataException($"JbMgr2.Item serialized to {result.Length} bytes; expected {ItemBinarySize}.");
            return result;
        }

        public readonly void Write(Span<byte> destination)
        {
            if (destination.Length < ItemBinarySize)
                throw new ArgumentException($"JbMgr2.Item requires {ItemBinarySize} bytes.", nameof(destination));
            ToBytes().CopyTo(destination);
        }
    }
}
