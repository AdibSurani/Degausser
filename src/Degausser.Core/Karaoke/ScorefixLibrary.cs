using System.Globalization;

namespace Degausser;

/// <summary>
/// The romaji scorefixes found in a folder of <c>.txt</c> files, keyed by the title ID (or
/// movie song ID) each one applies to. A file names its ID in its first line; a built-in ROM
/// song's file has no ID header and is named after the ID instead (<c>80010000.txt</c>). A scorefix
/// for a BDX has a <c>lyrics:</c> fingerprint header instead (see <see cref="Scorefix.Fingerprint(string)"/>).
/// Scorefixes are an optional extra: with no folder the library is simply empty.
/// <see cref="Current"/> is what the import, preview and catalogue code consult; the app sets it
/// at startup and whenever the folder changes. Core never looks for the folder itself.
/// </summary>
public sealed class ScorefixLibrary
{
    /// <summary>Larger than any valid scorefix (the lyrics are limited to 2,999 characters).</summary>
    private const long MaxFileBytes = 256 * 1024;

    private const int MaxFolderDepth = 4;

    private readonly Dictionary<uint, string> byId;
    private readonly HashSet<uint> withLyrics;
    private readonly Dictionary<string, string> byFingerprint;

    public static ScorefixLibrary Empty { get; } = new(null, new Dictionary<uint, string>(), [], new Dictionary<string, string>(), []);

    /// <summary>The library the app currently uses. Replace it after the folder changes.</summary>
    public static ScorefixLibrary Current { get; set; } = Empty;

    /// <summary>The folder the scorefixes were read from, or null for none.</summary>
    public string? Directory { get; }

    /// <summary>Files that were skipped, and why. A shared file you didn't write can be wrong.</summary>
    public IReadOnlyList<string> Problems { get; }

    public int Count => byId.Count + byFingerprint.Count;

    /// <summary>Scorefix text by title ID, ready for <see cref="Scorefix.Apply"/>.</summary>
    public IReadOnlyDictionary<uint, string> All => byId;

    private ScorefixLibrary(string? directory, Dictionary<uint, string> byId, HashSet<uint> withLyrics, Dictionary<string, string> byFingerprint, List<string> problems)
    {
        this.byFingerprint = byFingerprint;
        this.withLyrics = withLyrics;
        Directory = directory;
        this.byId = byId;
        Problems = problems;
    }

    public bool Has(uint titleId) => byId.ContainsKey(titleId);

    /// <summary>The scorefix also has romaji lyrics, not just a title (an instrumental's scorefix is title-only).</summary>
    public bool HasLyrics(uint titleId) => withLyrics.Contains(titleId);

    public string? Find(uint titleId) => byId.GetValueOrDefault(titleId);

    /// <summary>
    /// The scorefix for <paramref name="record"/>. An official song is found by its title ID. A custom
    /// song's ID (a BDX's) identifies nothing, so it is found by the fingerprint of its Japanese lyrics;
    /// a song with no lyrics has none to match.
    /// </summary>
    public string? Find(BBPRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        uint id = record.mgrItem.titleID;
        if (!Scorefix.IsCustomId(id) && byId.TryGetValue(id, out string? byTitle)) return byTitle;
        return Scorefix.Fingerprint(record) is string fingerprint ? byFingerprint.GetValueOrDefault(fingerprint) : null;
    }

    /// <summary>
    /// Reads every <c>.txt</c> in <paramref name="directory"/> and its subfolders (a README is
    /// ignored). A file that is unreadable, doesn't parse or has no title ID is skipped and
    /// listed in <see cref="Problems"/>; when two files name the same ID the later path wins.
    /// A missing folder gives an empty library.
    /// </summary>
    public static ScorefixLibrary FromDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
            return Empty;

        var byId = new Dictionary<uint, string>();
        var withLyrics = new HashSet<uint>();
        var source = new Dictionary<uint, string>();
        var byFingerprint = new Dictionary<string, string>();
        var fingerprintSource = new Dictionary<string, string>();
        var problems = new List<string>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = MaxFolderDepth, IgnoreInaccessible = true };
        IEnumerable<string> files = System.IO.Directory.EnumerateFiles(directory, "*", options)
            .Where(f => Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileNameWithoutExtension(f).StartsWith("README", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        foreach (string path in files)
        {
            string name = Path.GetRelativePath(directory, path);
            try
            {
                if (new FileInfo(path).Length > MaxFileBytes)
                    throw new FormatException("the file is too large to be a scorefix");
                string text = File.ReadAllText(path);
                ScorefixDocument document = ScorefixDocument.Parse(text);
                if (document.Fingerprint is string fingerprint)
                {
                    if (fingerprintSource.TryGetValue(fingerprint, out string? earlierFile))
                        problems.Add($"{earlierFile} and {name} are for the same lyrics; using {name}.");
                    byFingerprint[fingerprint] = text;
                    fingerprintSource[fingerprint] = name;
                    continue;
                }
                uint? id = document.TitleId
                    ?? (uint.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint fromName) ? fromName : null);
                if (id is not uint titleId)
                    throw new FormatException("it has no title ID (the first line, or the file name)");
                if (source.TryGetValue(titleId, out string? earlier))
                    problems.Add($"{earlier} and {name} are both for {titleId:X8}; using {name}.");
                byId[titleId] = text;
                source[titleId] = name;
                if (document.Lyrics.Length > 0) withLyrics.Add(titleId); else withLyrics.Remove(titleId);
            }
            catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                problems.Add($"{name}: {exception.Message}");
            }
        }
        return new ScorefixLibrary(directory, byId, withLyrics, byFingerprint, problems);
    }
}
