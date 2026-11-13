using Kermalis.EndianBinaryIO;

namespace Degausser;

/// <summary>
/// Fixed 68-byte header at the start of a Band Brothers P pack.
/// The final eight words are reserved/unknown and are zero in all 9,722 official
/// BBPs audited in 2026. They are still retained verbatim so a lossless rewrite
/// does not discard future, homebrew, or malformed data.
/// </summary>
public sealed class BbpPackHeader
{
    public const int BinarySize = 17 * sizeof(int);
    public const int UnknownWordCount = 8;
    public const int SupportedVersion = 0x00020001;

    public int Version { get; set; } = SupportedVersion;
    public int GakPresent { get; set; } = 1;
    public int GakUncompressedSize { get; set; } = Gak.BinarySize;
    public int GakOffset { get; set; } = BinarySize;
    public int GakCompressedSize { get; set; }
    public int VocaloidPresent { get; set; }
    public int VocaloidUncompressedSize { get; set; }
    public int VocaloidOffset { get; set; }
    public int VocaloidCompressedSize { get; set; }

    [BinaryArrayFixedLength(UnknownWordCount)]
    public int[] UnknownWords { get; set; } = new int[UnknownWordCount];

    public static BbpPackHeader Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BinarySize)
            throw new ArgumentException($"Pack header requires {BinarySize} bytes.", nameof(bytes));

        using var stream = new MemoryStream(bytes[..BinarySize].ToArray(), writable: false);
        var reader = new EndianBinaryReader(stream, endianness: Endianness.LittleEndian);
        BbpPackHeader header = reader.ReadObject<BbpPackHeader>();

        if (stream.Position != BinarySize)
            throw new InvalidDataException($"Pack header parser consumed {stream.Position} bytes; expected {BinarySize}.");

        header.UnknownWords = NormalizeUnknownWords(header.UnknownWords);
        return header;
    }

    public byte[] ToBytes()
    {
        int[] originalUnknown = UnknownWords;
        try
        {
            UnknownWords = NormalizeUnknownWords(originalUnknown);
            using var stream = new MemoryStream(BinarySize);
            var writer = new EndianBinaryWriter(stream, endianness: Endianness.LittleEndian);
            writer.WriteObject(this);

            byte[] result = stream.ToArray();
            if (result.Length != BinarySize)
                throw new InvalidDataException($"Pack header serialized to {result.Length} bytes; expected {BinarySize}.");
            return result;
        }
        finally
        {
            UnknownWords = originalUnknown;
        }
    }

    public BbpPackHeader Clone()
        => new()
        {
            Version = Version,
            GakPresent = GakPresent,
            GakUncompressedSize = GakUncompressedSize,
            GakOffset = GakOffset,
            GakCompressedSize = GakCompressedSize,
            VocaloidPresent = VocaloidPresent,
            VocaloidUncompressedSize = VocaloidUncompressedSize,
            VocaloidOffset = VocaloidOffset,
            VocaloidCompressedSize = VocaloidCompressedSize,
            UnknownWords = NormalizeUnknownWords(UnknownWords)
        };

    private static int[] NormalizeUnknownWords(int[]? values)
    {
        var result = new int[UnknownWordCount];
        if (values is not null)
            values.AsSpan(0, Math.Min(values.Length, result.Length)).CopyTo(result);
        return result;
    }
}
