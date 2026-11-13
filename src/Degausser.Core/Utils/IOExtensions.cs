using System.IO.Compression;

namespace Degausser;

internal static class IOExtensions
{
    /// <summary>
    /// GZip-compress data and patch the two cosmetic header bytes used by the
    /// game's pack files. The payload is standard GZip data.
    /// </summary>
    public static byte[] Compress(this ReadOnlySpan<byte> data)
    {
        using var compressedStream = new MemoryStream();
        using (var zipStream = new GZipStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            zipStream.Write(data);

        byte[] result = compressedStream.ToArray();
        if (result.Length >= 10)
        {
            result[8] = 0;
            result[9] = 3;
        }
        return result;
    }

    public static byte[] Compress(this byte[] data) => Compress((ReadOnlySpan<byte>)data);

    public static byte[] Decompress(this Stream stream)
    {
        using var zipStream = new GZipStream(stream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();
        zipStream.CopyTo(resultStream);
        return resultStream.ToArray();
    }

    public static byte[] Decompress(this byte[] buffer)
        => new MemoryStream(buffer, writable: false).Decompress();

    public static byte[] Decompress(this byte[] buffer, int index, int count)
        => new MemoryStream(buffer, index, count, writable: false).Decompress();
}
