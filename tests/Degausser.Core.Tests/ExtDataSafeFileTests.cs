using System.Buffers.Binary;
using System.IO.Compression;

namespace Degausser.Core.Tests;

public class ExtDataSafeFileTests
{
    [Fact]
    public void MirroredWriterPreservesGzipLengthInLastFourBytes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "degausser-safefile-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "jb", "mgr.bin");
        const int containerSize = 8192;
        byte[] payload = new byte[4096];
        new Random(12345).NextBytes(payload);

        try
        {
            ExtDataSafeFile.WriteMirrored(path, payload, containerSize);
            byte[] primary = File.ReadAllBytes(path);
            byte[] mirror = File.ReadAllBytes(ExtDataSafeFile.MirrorPath(path));
            Assert.Equal(containerSize, primary.Length);
            Assert.Equal(primary, mirror);

            int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(primary.AsSpan(containerSize - 4));
            Assert.InRange(compressedLength, 18, containerSize - 4);
            Assert.True(primary.AsSpan(compressedLength, containerSize - compressedLength - 4)
                .ToArray().All(b => b == 0));
            Assert.Equal(payload, ExtDataSafeFile.ReadDecompressed(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriterReservesFourBytesForFooter()
    {
        string directory = Path.Combine(Path.GetTempPath(), "degausser-safefile-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "mgr.bin");
        byte[] payload = new byte[256];
        new Random(42).NextBytes(payload);
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(payload);
        int compressedLength = checked((int)compressed.Length);
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                ExtDataSafeFile.WriteMirrored(path, payload, compressedLength + 3));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
