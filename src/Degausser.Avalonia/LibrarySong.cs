using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Degausser;

internal enum LibraryItemKind
{
    Bbp,
    Bdx,
    Bbpm,
    Bbpv
}

/// <summary>
/// Lightweight entry for the local BBP/BDX library. Scanning reads only format
/// metadata; the complete score is loaded lazily for preview/import.
/// </summary>
internal sealed partial class LibrarySong
{
    private WeakReference<BBPRecord>? _recordCache;
    private readonly string _searchText;
    private readonly string _compactSearchText;
    private readonly string _simpleKanaSearchKey;
    private readonly string _simpleRomajiSearchKey;

    private LibrarySong(
        string fullPath,
        LibraryItemKind kind,
        string title,
        string titleSimple,
        string author,
        int instruments,
        bool hasKaraoke,
        bool hasGuitar,
        bool hasPiano,
        uint? titleId = null,
        BBPRecord? cachedRecord = null)
    {
        FullPath = fullPath;
        Kind = kind;
        Title = (title ?? string.Empty).Replace("\n", string.Empty);
        TitleSimple = (titleSimple ?? string.Empty).Replace("\n", string.Empty);
        Author = author ?? string.Empty;
        Instruments = Math.Clamp(instruments, 0, 10);
        HasKaraoke = hasKaraoke;
        HasGuitar = hasGuitar;
        HasPiano = hasPiano;
        TitleId = titleId;

        if (cachedRecord is not null)
            _recordCache = new WeakReference<BBPRecord>(cachedRecord);

        _searchText = NormalizeSearchText(string.Join('\u001F', Filename, Folder, Title, TitleSimple, Author, Format,
            Language.Romanize(TitleSimple)));
        _compactSearchText = CompactSearchText(_searchText);
        _simpleKanaSearchKey = SimpleKanaSearchKey(TitleSimple);
        _simpleRomajiSearchKey = SimpleRomajiSearchKey(Language.Romanize(TitleSimple));
    }

    public string FullPath { get; }
    public string Filename => Path.GetFileName(FullPath);
    public string Folder => Path.GetFileName(Path.GetDirectoryName(FullPath)) ?? string.Empty;
    public string Title { get; }
    public string TitleSimple { get; }
    public string Author { get; }
    public bool HasKaraoke { get; }
    public bool HasGuitar { get; }
    public bool HasPiano { get; }
    public int Instruments { get; }
    public uint? TitleId { get; }
    public LibraryItemKind Kind { get; }
    public bool IsBdx => Kind == LibraryItemKind.Bdx;
    public bool IsBbpm => Kind == LibraryItemKind.Bbpm;
    public bool IsBbpv => Kind == LibraryItemKind.Bbpv;
    public bool CanPreviewSong => Kind != LibraryItemKind.Bbpv;
    public string Format => Kind switch
    {
        LibraryItemKind.Bbp => "BBP",
        LibraryItemKind.Bdx => "BDX",
        LibraryItemKind.Bbpm => "BBPM",
        LibraryItemKind.Bbpv => "BBPV",
        _ => "?"
    };

    public static LibrarySong Read(string path)
    {
        path = Path.GetFullPath(path);
        string extension = Path.GetExtension(path);

        if (extension.Equals(".bbp", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: JbMgr.Item.ItemBinarySize,
                options: FileOptions.SequentialScan);

            if (stream.Length < JbMgr.Item.ItemBinarySize + BbpPackHeader.BinarySize)
                throw new InvalidDataException($"BBP is too small ({stream.Length} bytes).");

            Span<byte> itemBytes = stackalloc byte[JbMgr.Item.ItemBinarySize];
            stream.ReadExactly(itemBytes);
            JbMgr.Item item = JbMgr.Item.Read(itemBytes);
            return FromManagerItem(path, item);
        }

        if (extension.Equals(".bdx", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: BdxMetadataReader.PrefixSize,
                options: FileOptions.SequentialScan);

            if (stream.Length < BdxMetadataReader.PrefixSize)
                throw new InvalidDataException($"BDX is too small (0x{stream.Length:X} bytes).");

            Span<byte> prefix = stackalloc byte[BdxMetadataReader.PrefixSize];
            stream.ReadExactly(prefix);
            if (BdxToBbpConverter.IsLegacyBbs(prefix))
                throw new InvalidDataException(
                    "This .bdx file is actually a legacy BBRS_GAK/.bbs song and is not a Band Brothers DX BDX.");
            if (stream.Length < BdxToBbpConverter.MinimumConvertibleBdxSize)
                throw new InvalidDataException(
                    $"BDX size is 0x{stream.Length:X}; at least 0x{BdxToBbpConverter.MinimumConvertibleBdxSize:X} bytes are required.");

            BdxMetadata metadata = BdxMetadataReader.Read(prefix, UserSettingsStore.Current.BdxDefaultAuthor);
            return new LibrarySong(
                path,
                LibraryItemKind.Bdx,
                metadata.Title,
                metadata.Title,
                metadata.Contributor,
                metadata.Parts,
                metadata.HasKaraoke,
                metadata.HasGuitar,
                metadata.HasPiano,
                titleId: null);
        }

        if (extension.Equals(".bbpm", StringComparison.OrdinalIgnoreCase))
        {
            BbpmPackage package = BbpmPackage.Open(path);
            JbMgr.Item item = package.JbItem.item;
            return new LibrarySong(
                path,
                LibraryItemKind.Bbpm,
                package.Title,
                package.TitleSimple,
                package.Author,
                item.flags.Parts,
                item.flags.HasLyrics,
                item.flags.HasGuitar,
                item.flags.HasPiano,
                item.titleID);
        }

        if (extension.Equals(".bbpv", StringComparison.OrdinalIgnoreCase))
        {
            BbpvPackage package = BbpvPackage.Open(path);
            return new LibrarySong(
                path,
                LibraryItemKind.Bbpv,
                package.Artist,
                package.Artist,
                $"Vocaloid · {package.DatabaseBaseName}",
                0,
                false,
                false,
                false);
        }

        throw new InvalidDataException($"Unsupported library format: {extension}");
    }

    private static LibrarySong FromManagerItem(string path, JbMgr.Item item, BBPRecord? cachedRecord = null)
        => new(
            path,
            LibraryItemKind.Bbp,
            item.title ?? string.Empty,
            item.titleSimple ?? string.Empty,
            item.author ?? string.Empty,
            item.flags.Parts,
            item.flags.HasLyrics,
            item.flags.HasGuitar,
            item.flags.HasPiano,
            item.titleID,
            cachedRecord);

    public BBPRecord LoadRecord()
    {
        if (_recordCache is not null && _recordCache.TryGetTarget(out BBPRecord? cached))
            return cached;

        BBPRecord record = Kind switch
        {
            LibraryItemKind.Bdx => BBPRecord.FromBDX(FullPath, UserSettingsStore.Current.BdxOptions),
            LibraryItemKind.Bbp => BBPRecord.FromBBP(FullPath),
            LibraryItemKind.Bbpm => BbpmPackage.Open(FullPath).LoadSongRecord(),
            LibraryItemKind.Bbpv => throw new InvalidOperationException("BBPV is a singer package and has no song to preview."),
            _ => throw new InvalidOperationException("Unknown library item kind.")
        };
        _recordCache = new WeakReference<BBPRecord>(record);
        return record;
    }

    public bool MatchesSearch(string? query) => MatchesSearch(SearchQuery.Create(query));

    /// <summary>For scanning many songs: derive the query's keys once, not once per song.</summary>
    public bool MatchesSearch(SearchQuery query)
        => MatchesSearchCore(
            query,
            _searchText,
            _compactSearchText,
            _simpleKanaSearchKey,
            _simpleRomajiSearchKey);

    public static bool MatchesManagerItem(JbMgr.Item item, string? query, string? extra = null)
    {
        string title = item.title ?? string.Empty;
        string titleSimple = item.titleSimple ?? string.Empty;
        string author = item.author ?? string.Empty;
        string searchText = NormalizeSearchText(string.Join('\u001F', title, titleSimple, author,
            Language.Romanize(titleSimple), extra ?? string.Empty));

        return MatchesSearchCore(
            query,
            searchText,
            CompactSearchText(searchText),
            SimpleKanaSearchKey(titleSimple),
            SimpleRomajiSearchKey(Language.Romanize(titleSimple)));
    }

    private static bool MatchesSearchCore(
        string? query,
        string searchText,
        string compactSearchText,
        string simpleKanaSearchKey,
        string simpleRomajiSearchKey)
        => MatchesSearchCore(SearchQuery.Create(query), searchText, compactSearchText, simpleKanaSearchKey, simpleRomajiSearchKey);

    private static bool MatchesSearchCore(
        SearchQuery query,
        string searchText,
        string compactSearchText,
        string simpleKanaSearchKey,
        string simpleRomajiSearchKey)
    {
        if (query.IsEmpty) return true;
        if (searchText.Contains(query.Normalized, StringComparison.Ordinal)) return true;
        if (query.Compact.Length > 0 && compactSearchText.Contains(query.Compact, StringComparison.Ordinal)) return true;
        if (query.Kana is not null && simpleKanaSearchKey.Contains(query.Kana, StringComparison.Ordinal)) return true;
        if (query.Romaji is not null && simpleRomajiSearchKey.Contains(query.Romaji, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>A search string reduced to the keys a song is compared against.</summary>
    public sealed class SearchQuery
    {
        public string Normalized { get; }
        public string Compact { get; }
        /// <summary>The kana key, only when the query has kana in it and the key isn't empty.</summary>
        public string? Kana { get; }
        /// <summary>The romaji key, only when the query looks like romaji and the key isn't empty.</summary>
        public string? Romaji { get; }
        public bool IsEmpty => Normalized.Length == 0 || string.IsNullOrWhiteSpace(Normalized);

        private SearchQuery(string normalized)
        {
            Normalized = normalized;
            if (IsEmpty) { Compact = string.Empty; return; }
            Compact = CompactSearchText(normalized);
            string kana = SimpleKanaSearchKey(normalized);
            Kana = ContainsKana(normalized) && kana.Length > 0 ? kana : null;
            if (LooksLikeRomaji(normalized))
            {
                string romaji = SimpleRomajiSearchKey(normalized);
                Romaji = romaji.Length > 0 ? romaji : null;
            }
        }

        public static SearchQuery Create(string? query) => new(NormalizeSearchText(query?.Trim()));
    }

    public static string NormalizeSearchText(string? value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static string CompactSearchText(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
            if (char.IsLetterOrDigit(c)) builder.Append(c);
        return builder.ToString();
    }

    /// <summary>
    /// Mirrors the deliberate simplification found in JbMgr.titleSimple:
    /// hiragana/katakana are unified, dakuten/handakuten are ignored, and
    /// small kana are treated as their full-size counterparts.
    /// </summary>
    private static string SimpleKanaSearchKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string decomposed = value.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char raw in decomposed)
        {
            if (raw is '\u3099' or '\u309A') continue; // dakuten / handakuten

            char c = raw;
            if (c >= 'ぁ' && c <= 'ゖ') c = (char)(c - 'ぁ' + 'ァ');
            c = c switch
            {
                'ァ' => 'ア', 'ィ' => 'イ', 'ゥ' => 'ウ', 'ェ' => 'エ', 'ォ' => 'オ',
                'ャ' => 'ヤ', 'ュ' => 'ユ', 'ョ' => 'ヨ', 'ッ' => 'ツ', 'ヮ' => 'ワ',
                _ => c
            };

            if (char.IsLetterOrDigit(c)) builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>
    /// Loose Latin key matching the same distinctions titleSimple discards.
    /// This is intentionally a search heuristic, not a general romanizer.
    /// </summary>
    private static string SimpleRomajiSearchKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string s = new string(value.Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant()
            .Where(c => c is >= 'a' and <= 'z' || char.IsDigit(c))
            .ToArray());
        if (s.Length == 0) return string.Empty;

        // A small っ becomes full-sized ツ in titleSimple. Expand common
        // doubled-consonant Hepburn spelling back to an explicit "tsu" first.
        string previous;
        do
        {
            previous = s;
            s = DoubledConsonantRegex().Replace(s, "tsu$1");
        } while (!ReferenceEquals(previous, s) && previous != s);

        // titleSimple expands small ャュョ, so contracted Hepburn forms need
        // the same treatment before voicing distinctions are collapsed.
        (string From, string To)[] yoon =
        [
            ("sha", "shiya"), ("shu", "shiyu"), ("sho", "shiyo"),
            ("cha", "chiya"), ("chu", "chiyu"), ("cho", "chiyo"),
            ("ja", "shiya"), ("ju", "shiyu"), ("jo", "shiyo"),
            ("kya", "kiya"), ("kyu", "kiyu"), ("kyo", "kiyo"),
            ("gya", "kiya"), ("gyu", "kiyu"), ("gyo", "kiyo"),
            ("nya", "niya"), ("nyu", "niyu"), ("nyo", "niyo"),
            ("hya", "hiya"), ("hyu", "hiyu"), ("hyo", "hiyo"),
            ("bya", "hiya"), ("byu", "hiyu"), ("byo", "hiyo"),
            ("pya", "hiya"), ("pyu", "hiyu"), ("pyo", "hiyo"),
            ("mya", "miya"), ("myu", "miyu"), ("myo", "miyo"),
            ("rya", "riya"), ("ryu", "riyu"), ("ryo", "riyo")
        ];
        foreach ((string from, string to) in yoon) s = s.Replace(from, to, StringComparison.Ordinal);

        (string From, string To)[] simplifications =
        [
            ("dji", "shi"), ("dzu", "su"), ("chi", "shi"), ("tsu", "su"), ("ji", "shi"),
            ("ga", "ka"), ("gi", "ki"), ("gu", "ku"), ("ge", "ke"), ("go", "ko"),
            ("za", "sa"), ("zu", "su"), ("ze", "se"), ("zo", "so"),
            ("da", "ta"), ("de", "te"), ("do", "to"),
            ("ba", "ha"), ("bi", "hi"), ("bu", "fu"), ("be", "he"), ("bo", "ho"),
            ("pa", "ha"), ("pi", "hi"), ("pu", "fu"), ("pe", "he"), ("po", "ho")
        ];
        foreach ((string from, string to) in simplifications) s = s.Replace(from, to, StringComparison.Ordinal);
        return s.ToUpperInvariant();
    }

    private static bool ContainsKana(string value)
        => value.Any(c => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヿ');

    private static bool LooksLikeRomaji(string value)
    {
        bool hasLetter = false;
        foreach (char c in value)
        {
            if (c is >= 'A' and <= 'Z') { hasLetter = true; continue; }
            if (char.IsDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)) continue;
            return false;
        }
        return hasLetter;
    }

    [GeneratedRegex(@"([BCDFGHJKLMNPQRSTVWXYZbcdfghjklmnpqrstvwxyz])\1", RegexOptions.CultureInvariant)]
    private static partial Regex DoubledConsonantRegex();
}
