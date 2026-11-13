using Kermalis.EndianBinaryIO;

namespace Degausser;

/// <summary>Secondary singer manager: SingerMgr.Item plus 64 unknown bytes.</summary>
public class SingerMgr2
{
    public const int HeaderSize = 4;
    public const int ItemCount = 1011;
    public const int BinarySize = HeaderSize + (Item.ItemBinarySize * ItemCount);

    private sealed class DiskManager
    {
        public DiskManager() { }
        public int Magic { get; set; }

        [BinaryArrayFixedLength(ItemCount)]
        public Item.DiskItem[] Items { get; set; } =
            Enumerable.Range(0, ItemCount).Select(_ => new Item.DiskItem()).ToArray();
    }

    public int magic;
    public Item[] items = [];

    public static SingerMgr2 Empty => CreateEmpty();

    public static SingerMgr2 CreateEmpty()
        => new()
        {
            magic = 0x10002,
            items = Enumerable.Range(0, ItemCount).Select(_ => Item.CreateEmpty()).ToArray()
        };

    public static SingerMgr2 Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BinarySize)
            throw new ArgumentException($"SingerMgr2 requires {BinarySize} bytes.", nameof(bytes));

        using var stream = new MemoryStream(bytes[..BinarySize].ToArray(), writable: false);
        var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
        DiskManager disk = reader.ReadObject<DiskManager>();

        if (stream.Position != BinarySize)
            throw new InvalidDataException($"SingerMgr2 parser consumed {stream.Position} bytes; expected {BinarySize}.");

        return new SingerMgr2
        {
            magic = disk.Magic,
            items = disk.Items.Select(Item.FromDisk).ToArray()
        };
    }

    public byte[] ToBytes()
    {
        var disk = new DiskManager
        {
            Magic = magic,
            Items = Enumerable.Range(0, ItemCount)
                .Select(i => (i < items.Length ? items[i] : Item.Empty).ToDisk())
                .ToArray()
        };

        using var stream = new MemoryStream(BinarySize);
        var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
        writer.WriteObject(disk);

        byte[] result = stream.ToArray();
        if (result.Length != BinarySize)
            throw new InvalidDataException($"SingerMgr2 serialized to {result.Length} bytes; expected {BinarySize}.");
        return result;
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length < BinarySize)
            throw new ArgumentException($"SingerMgr2 requires {BinarySize} bytes.", nameof(destination));
        ToBytes().CopyTo(destination);
    }

    public struct Item
    {
        public const int ItemBinarySize = SingerMgr.Item.ItemBinarySize + 64;

        internal sealed class DiskItem
        {
            public DiskItem() { }
            public SingerMgr.Item.DiskItem Item { get; set; } = new();
            public ulong Unk1 { get; set; }
            public ulong Unk2 { get; set; }
            public ulong Unk3 { get; set; }
            public ulong Unk4 { get; set; }
            public ulong Unk5 { get; set; }
            public ulong Unk6 { get; set; }
            public ulong Unk7 { get; set; }
            public ulong Unk8 { get; set; }
        }

        public SingerMgr.Item item;
        public ulong unk1;
        public ulong unk2;
        public ulong unk3;
        public ulong unk4;
        public ulong unk5;
        public ulong unk6;
        public ulong unk7;
        public ulong unk8;

        public static Item Empty => CreateEmpty();
        public static Item CreateEmpty() => new() { item = SingerMgr.Item.CreateEmpty() };

        public static Item Read(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < ItemBinarySize)
                throw new ArgumentException($"SingerMgr2.Item requires {ItemBinarySize} bytes.", nameof(bytes));

            using var stream = new MemoryStream(bytes[..ItemBinarySize].ToArray(), writable: false);
            var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
            return FromDisk(reader.ReadObject<DiskItem>());
        }

        internal static Item FromDisk(DiskItem raw)
            => new()
            {
                item = SingerMgr.Item.FromDisk(raw.Item),
                unk1 = raw.Unk1,
                unk2 = raw.Unk2,
                unk3 = raw.Unk3,
                unk4 = raw.Unk4,
                unk5 = raw.Unk5,
                unk6 = raw.Unk6,
                unk7 = raw.Unk7,
                unk8 = raw.Unk8
            };

        internal readonly DiskItem ToDisk()
            => new()
            {
                Item = item.ToDisk(),
                Unk1 = unk1,
                Unk2 = unk2,
                Unk3 = unk3,
                Unk4 = unk4,
                Unk5 = unk5,
                Unk6 = unk6,
                Unk7 = unk7,
                Unk8 = unk8
            };

        public readonly byte[] ToBytes()
        {
            using var stream = new MemoryStream(ItemBinarySize);
            var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
            writer.WriteObject(ToDisk());

            byte[] result = stream.ToArray();
            if (result.Length != ItemBinarySize)
                throw new InvalidDataException($"SingerMgr2.Item serialized to {result.Length} bytes; expected {ItemBinarySize}.");
            return result;
        }

        public readonly void Write(Span<byte> destination)
        {
            if (destination.Length < ItemBinarySize)
                throw new ArgumentException($"SingerMgr2.Item requires {ItemBinarySize} bytes.", nameof(destination));
            ToBytes().CopyTo(destination);
        }

        /// <summary>
        /// Reproduce the game's save-side projection into the legacy 0x19C-byte
        /// singer manager item. This rule was validated against all 1011 records
        /// of the recovered extdata: applying it to mgr2 recreates mgr byte-for-byte.
        /// </summary>
        public readonly SingerMgr.Item ToLegacyItem()
        {
            SingerMgr.Item legacy = item;
            ushort extensionValue = unchecked((ushort)(unk1 & 0xFFFF));
            uint word = legacy.unk7;
            uint value = extensionValue is >= 5 and <= 24
                ? extensionValue
                : (word & 3) == 1 ? 21u : 11u;
            legacy.unk7 = (word & ~0x000001FCu) | ((value << 2) & 0x000001FCu);
            return legacy;
        }
    }
}
