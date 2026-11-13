using System.Buffers.Binary;

namespace Degausser;

/// <summary>
/// Host-side counterpart of the game's SafeFile convention. Manager files are
/// gzip streams padded to a fixed container size and mirrored to name_.ext.
/// The final four bytes store the length of the gzip stream (little-endian).
/// </summary>
public static class ExtDataSafeFile
{
    public static byte[] ReadDecompressed(string path)
    {
        Exception? primaryError = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return stream.Decompress();
        }
        catch (Exception exception)
        {
            primaryError = exception;
        }

        string mirror = MirrorPath(path);
        try
        {
            using var stream = new FileStream(mirror, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return stream.Decompress();
        }
        catch (Exception mirrorError)
        {
            throw new IOException($"Could not read either SafeFile copy '{path}' or '{mirror}'.", new AggregateException(primaryError!, mirrorError));
        }
    }

    /// <summary>
    /// The exact container bytes for a manager: gzip stream, zero padding, and
    /// the little-endian compressed length in the final four bytes. The 3DS uses
    /// that footer when loading; if it is left as zero, the gzip data still
    /// decompresses on a PC but the game rejects the whole save (observed after
    /// deleting a song).
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> uncompressed, int containerSize)
    {
        byte[] compressed = uncompressed.Compress();
        if (containerSize < sizeof(int) || compressed.Length > containerSize - sizeof(int))
            throw new InvalidDataException($"Compressed manager is {compressed.Length:N0} bytes; fixed container is only {containerSize:N0} bytes including its 4-byte length footer.");
        byte[] container = new byte[containerSize];
        compressed.CopyTo(container, 0);
        BinaryPrimitives.WriteInt32LittleEndian(container.AsSpan(containerSize - sizeof(int)), compressed.Length);
        return container;
    }

    /// <summary>Decompresses a container read by other means (e.g. a browser file handle).</summary>
    public static byte[] Decode(byte[] container) => container.Decompress();

    public static void WriteMirrored(string path, ReadOnlySpan<byte> uncompressed, int containerSize)
    {
        byte[] container = Encode(uncompressed, containerSize);
        string full = Path.GetFullPath(path);
        WriteFileAtomic(full, container);
        WriteFileAtomic(MirrorPath(full), container);
    }

    public static string MirrorPath(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        string file = Path.GetFileName(path);
        int dot = file.LastIndexOf('.');
        string mirror = dot >= 0 ? file.Insert(dot, "_") : file + "_";
        return string.IsNullOrEmpty(directory) ? mirror : Path.Combine(directory, mirror);
    }

    /// <summary>Writes through a .tmp file and renames it over the target, so a crash never leaves half a file.</summary>
    internal static void WriteFileAtomic(string path, byte[] bytes)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
