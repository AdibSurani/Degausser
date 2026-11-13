using System.Globalization;
using System.Text;

namespace Degausser;

/// <summary>
/// Tiny uncompressed package framing used by BBPM/BBPV.
/// The ASCII header lists decimal byte lengths and names; payloads follow in order.
/// </summary>
public sealed class SimplePackage
{
    private const int MaxHeaderBytes = 64 * 1024;

    public sealed record Entry(string Name, long Length, long Offset);

    public string FilePath { get; }
    public string Magic { get; }
    public int Version { get; }
    public IReadOnlyList<Entry> Entries => entries;

    private readonly List<Entry> entries;
    private readonly Dictionary<string, Entry> byName;

    private SimplePackage(string filePath, string magic, int version, List<Entry> entries)
    {
        FilePath = filePath;
        Magic = magic;
        Version = version;
        this.entries = entries;
        byName = entries.ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    public static SimplePackage Open(string path, string expectedMagic)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var headerLines = new List<string>();
        int headerBytes = 0;
        while (true)
        {
            string line = ReadAsciiLine(stream, ref headerBytes);
            if (line.Length == 0) break;
            headerLines.Add(line);
        }

        if (headerLines.Count == 0)
            throw new InvalidDataException("Package header is empty.");

        string[] first = headerLines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (first.Length != 2 || !int.TryParse(first[1], NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            throw new InvalidDataException("Invalid package signature line.");
        if (!first[0].Equals(expectedMagic, StringComparison.Ordinal))
            throw new InvalidDataException($"Expected {expectedMagic} package, found {first[0]}.");
        if (version != 1)
            throw new InvalidDataException($"Unsupported {expectedMagic} version {version}.");

        long payloadOffset = stream.Position;
        long cursor = payloadOffset;
        var entries = new List<Entry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in headerLines.Skip(1))
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1)
                throw new InvalidDataException($"Invalid package entry line: {line}");
            if (!long.TryParse(line.AsSpan(0, tab), NumberStyles.None, CultureInfo.InvariantCulture, out long length) || length < 0
                || length > long.MaxValue - cursor)
                throw new InvalidDataException($"Invalid package entry length: {line}");
            string name = line[(tab + 1)..];
            ValidateName(name);
            if (!names.Add(name))
                throw new InvalidDataException($"Duplicate package entry: {name}");
            entries.Add(new Entry(name, length, cursor));
            cursor = checked(cursor + length);
        }

        if (entries.Count == 0)
            throw new InvalidDataException("Package contains no entries.");
        if (cursor != stream.Length)
            throw new InvalidDataException($"Package payload lengths end at {cursor:N0} bytes, but file size is {stream.Length:N0} bytes.");

        return new SimplePackage(Path.GetFullPath(path), first[0], version, entries);
    }

    public Entry GetEntry(string name)
        => byName.TryGetValue(name, out Entry? entry)
            ? entry
            : throw new InvalidDataException($"Package is missing required entry '{name}'.");

    public bool TryGetEntry(string name, out Entry? entry) => byName.TryGetValue(name, out entry);

    public Stream OpenEntry(string name)
    {
        Entry entry = GetEntry(name);
        var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        file.Position = entry.Offset;
        return new SliceReadStream(file, entry.Length);
    }

    public byte[] ReadEntryBytes(string name, int? exactLength = null)
    {
        Entry entry = GetEntry(name);
        if (entry.Length > int.MaxValue)
            throw new InvalidDataException($"Entry '{name}' is too large to read into memory.");
        if (exactLength is int expected && entry.Length != expected)
            throw new InvalidDataException($"Entry '{name}' is {entry.Length} bytes; expected {expected}.");

        byte[] result = new byte[(int)entry.Length];
        using Stream input = OpenEntry(name);
        input.ReadExactly(result);
        return result;
    }

    public void CopyEntryTo(string name, string destination)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        using Stream input = OpenEntry(name);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    public static void Write(string path, string magic, IReadOnlyList<WriteEntry> entries)
    {
        if (entries.Count == 0) throw new ArgumentException("At least one entry is required.", nameof(entries));
        if (entries.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new ArgumentException("Package entry names must be unique.", nameof(entries));
        foreach (WriteEntry entry in entries) ValidateName(entry.Name);

        var header = new StringBuilder();
        header.Append(magic).Append(" 1\n");
        foreach (WriteEntry entry in entries)
            header.Append(entry.Length.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(entry.Name).Append('\n');
        header.Append('\n');
        byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        if (headerBytes.Length > MaxHeaderBytes)
            throw new InvalidDataException("Package header is too large.");

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = fullPath + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                output.Write(headerBytes);
                foreach (WriteEntry entry in entries)
                {
                    using Stream input = entry.Open();
                    CopyExact(input, output, entry.Length, entry.Name);
                }
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public sealed class WriteEntry
    {
        private readonly Func<Stream> opener;
        public string Name { get; }
        public long Length { get; }
        public WriteEntry(string name, byte[] bytes)
        {
            Name = name;
            Length = bytes.LongLength;
            opener = () => new MemoryStream(bytes, writable: false);
        }
        public WriteEntry(string name, string filePath)
        {
            Name = name;
            Length = new FileInfo(filePath).Length;
            opener = () => new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        internal Stream Open() => opener();
    }

    private static void CopyExact(Stream input, Stream output, long length, string name)
    {
        byte[] buffer = new byte[128 * 1024];
        long remaining = length;
        while (remaining > 0)
        {
            int request = (int)Math.Min(buffer.Length, remaining);
            int read = input.Read(buffer, 0, request);
            if (read <= 0) throw new EndOfStreamException($"Entry source '{name}' ended early.");
            output.Write(buffer, 0, read);
            remaining -= read;
        }
        if (input.ReadByte() != -1)
            throw new InvalidDataException($"Entry source '{name}' grew while packaging.");
    }

    private static string ReadAsciiLine(Stream stream, ref int bytesRead)
    {
        var bytes = new List<byte>();
        while (true)
        {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("Package header ended before its blank-line terminator.");
            bytesRead++;
            if (bytesRead > MaxHeaderBytes) throw new InvalidDataException("Package header exceeds 64 KiB.");
            if (value == '\n') break;
            if (value == '\r') continue;
            if (value > 0x7F) throw new InvalidDataException("Package header must be ASCII.");
            bytes.Add((byte)value);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains('\t') || name.Contains('\r') || name.Contains('\n'))
            throw new InvalidDataException($"Invalid package entry name '{name}'.");
        string normalized = name.Replace('\\', '/');
        if (normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Invalid package entry name '{name}'.");
        if (!name.All(c => c <= 0x7F))
            throw new InvalidDataException("Package entry names must be ASCII.");
    }

    private sealed class SliceReadStream : Stream
    {
        private readonly FileStream file;
        private long remaining;
        public SliceReadStream(FileStream file, long length) { this.file = file; remaining = length; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => remaining;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (remaining <= 0) return 0;
            int requested = (int)Math.Min(count, remaining);
            int read = file.Read(buffer, offset, requested);
            remaining -= read;
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            if (remaining <= 0) return 0;
            int requested = (int)Math.Min(buffer.Length, remaining);
            int read = file.Read(buffer[..requested]);
            remaining -= read;
            return read;
        }
        protected override void Dispose(bool disposing) { if (disposing) file.Dispose(); base.Dispose(disposing); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
