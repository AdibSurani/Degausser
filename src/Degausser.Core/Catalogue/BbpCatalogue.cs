using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Degausser;

/// <summary>
/// Searchable, browsable embedded view of the curated official BBP Catalogue v3.0.
/// The embedded resource is generated from the normalized catalogue export by
/// the maintainer's build-catalogue-index.py; it intentionally contains only fields useful
/// to Degausser's search, browse and lookup UI.
/// </summary>
public sealed class BbpCatalogue
{
    private const string ResourceSuffix = "Catalogue.bbp_catalogue_v3.0.compact.json.gz";
    private readonly Dictionary<uint, CatalogueArrangement> arrangementsByTitleId;
    private readonly Dictionary<uint, CatalogueSong> songsByTitleId;

    public string SchemaVersion { get; }
    public string CatalogueVersion { get; }
    public string GeneratedAt { get; }
    public IReadOnlyList<CatalogueSong> Songs { get; }
    public IReadOnlyList<CatalogueCollection> Collections { get; private set; }

    /// <summary>Songs with an arrangement that has a scorefix: English lyrics, or just an English title for songs with no karaoke. Empty, and not offered, until scorefixes are loaded.</summary>
    public CatalogueCollection Scorefixes { get; }

    /// <summary>Collections worth offering before anything is searched.</summary>
    public IReadOnlyList<CatalogueCollection> FeaturedCollections => Scorefixes.SongCount > 0 ? [Scorefixes] : [];

    public int ArrangementCount => arrangementsByTitleId.Count;

    private BbpCatalogue(CatalogueDocument document)
    {
        SchemaVersion = document.Schema ?? string.Empty;
        CatalogueVersion = document.Catalogue ?? string.Empty;
        GeneratedAt = document.GeneratedAt ?? string.Empty;
        Songs = document.Songs ?? [];
        List<CatalogueCollection> collections = document.Collections ?? [];

        for (int i = 0; i < collections.Count; i++)
        {
            CatalogueCollection collection = collections[i];
            if (collection.ParentIndex is int parent && parent >= 0 && parent < collections.Count && parent != i)
            {
                collection.Parent = collections[parent];
                collection.Parent.ChildList.Add(collection);
            }
            collection.PrepareSearch();
        }

        // Not part of the generated index: scorefixes come from a folder, not the catalogue.
        Scorefixes = new CatalogueCollection
        {
            Id = "feature:scorefixes",
            KindName = "feature",
            Name = "Scorefixes",
            Aliases = ["scorefix", "English lyrics", "English titles", "romaji", "English karaoke"],
            MediaTypes = ["scorefixes"]
        };
        Scorefixes.PrepareSearch();

        arrangementsByTitleId = new Dictionary<uint, CatalogueArrangement>();
        songsByTitleId = new Dictionary<uint, CatalogueSong>();
        foreach (CatalogueSong song in Songs)
        {
            song.PrepareSearch();
            song.ResolveMemberships(collections);
            foreach (CatalogueArrangement arrangement in song.Arrangements)
            {
                arrangementsByTitleId[arrangement.TitleId] = arrangement;
                songsByTitleId[arrangement.TitleId] = song;
            }
        }
        baseCollections = collections;
        Collections = collections;
        RefreshScorefixes();
    }

    private readonly List<CatalogueCollection> baseCollections;

    /// <summary>
    /// Rebuilds the Scorefixes collection from <see cref="ScorefixLibrary.Current"/>. Call it after the
    /// library is replaced. Not thread-safe: do it on the thread that reads the catalogue.
    /// </summary>
    public void RefreshScorefixes()
    {
        Scorefixes.SongCount = 0;
        foreach (CatalogueSong song in Songs)
            song.SetMembership(Scorefixes, song.Arrangements.Any(a => a.HasScorefix));
        Collections = Scorefixes.SongCount > 0 ? [.. baseCollections, Scorefixes] : baseCollections;
    }

    public static BbpCatalogue LoadEmbedded()
    {
        Assembly assembly = typeof(BbpCatalogue).Assembly;
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
            throw new InvalidOperationException($"Embedded BBP catalogue resource '*{ResourceSuffix}' was not found.");

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: false);
        CatalogueDocument? document = JsonSerializer.Deserialize(gzip, CatalogueJsonContext.Default.CatalogueDocument);
        if (document?.Songs is null || document.Songs.Count == 0)
            throw new InvalidDataException("Embedded BBP catalogue is empty or invalid.");
        return new BbpCatalogue(document);
    }

    public CatalogueArrangement? FindArrangement(uint titleId)
        => arrangementsByTitleId.GetValueOrDefault(titleId);

    /// <summary>The logical song an official arrangement belongs to.</summary>
    public CatalogueSong? FindSong(uint titleId)
        => songsByTitleId.GetValueOrDefault(titleId);

    /// <summary>
    /// Finds franchises, works, games, theme categories and artists by English
    /// or original name. Collections with more songs rank higher among equally
    /// good name matches.
    /// </summary>
    public IReadOnlyList<CatalogueCollection> SearchCollections(string? query, int? limit = null)
    {
        string normalized = NormalizeSearchText(query).Trim();
        if (normalized.Length == 0) return [];
        IEnumerable<CatalogueCollection> result = Collections
            .Select(c => (Collection: c, Score: c.Score(normalized)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Collection.SongCount)
            .ThenBy(x => x.Collection.Name, CatalogueStringComparer.Instance)
            .Select(x => x.Collection);
        if (limit is int count && count >= 0)
            result = result.Take(count);
        return result.ToList();
    }

    /// <summary>
    /// The songs in a collection and every collection beneath it (a franchise
    /// includes its games and works), ordered by sub-collection, then by usage
    /// such as opening/ending number, then title.
    /// </summary>
    public IReadOnlyList<CatalogueSongInCollection> SongsIn(CatalogueCollection collection)
    {
        var scope = collection.SelfAndDescendants().ToHashSet();
        var result = new List<CatalogueSongInCollection>();
        foreach (CatalogueSong song in Songs)
        {
            CatalogueMembership? best = null;
            foreach (CatalogueMembership membership in song.Memberships)
            {
                if (!scope.Contains(membership.Collection)) continue;
                // Prefer the most specific collection, then a membership with a usage.
                if (best is null
                    || (best.Collection == collection && membership.Collection != collection)
                    || (best.Collection == membership.Collection && best.Usage is null && membership.Usage is not null))
                    best = membership;
            }
            if (best is not null)
                result.Add(new CatalogueSongInCollection(song, best));
        }
        return result
            .OrderBy(x => x.Membership.Collection == collection ? 0 : 1)
            .ThenBy(x => x.Membership.Collection.Name, CatalogueStringComparer.Instance)
            .ThenBy(x => CatalogueMembership.UsageOrder(x.Membership.Usage))
            .ThenBy(x => x.Membership.Sequence ?? int.MaxValue)
            .ThenBy(x => x.Song.Title, CatalogueStringComparer.Instance)
            .ToList();
    }

    /// <summary>
    /// Searches the logical-song catalogue. All whitespace-delimited terms must
    /// match somewhere in the curated search corpus; results are then scored so
    /// exact/prefix title and artist matches float above incidental metadata hits.
    /// </summary>
    public IReadOnlyList<CatalogueSong> Search(string? query, int? limit = null)
    {
        string normalized = NormalizeSearchText(query).Trim();
        List<string> terms = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

        IEnumerable<(CatalogueSong Song, int Score)> matches = Songs.Select(song => (song, song.Score(terms, normalized)));
        if (terms.Count > 0)
            matches = matches.Where(x => x.Score >= 0);

        IOrderedEnumerable<(CatalogueSong Song, int Score)> ordered = terms.Count > 0
            ? matches.OrderByDescending(x => x.Score)
                .ThenBy(x => x.Song.Title, CatalogueStringComparer.Instance)
                .ThenBy(x => x.Song.ArtistText, CatalogueStringComparer.Instance)
            : matches.OrderBy(x => x.Song.Title, CatalogueStringComparer.Instance)
                .ThenBy(x => x.Song.ArtistText, CatalogueStringComparer.Instance);

        IEnumerable<CatalogueSong> result = ordered.Select(x => x.Song);
        if (limit is int count && count >= 0)
            result = result.Take(count);
        return result.ToList();
    }

    internal static string NormalizeSearchText(string? value) => NormalizeSearchText(value, TextFolding.UseRuntime);

    internal static string NormalizeSearchText(string? value, bool runtimeNormalisation)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!runtimeNormalisation)
            return TextFolding.StripLatinAccents(TextFolding.CompatibilityTable(value).ToUpperInvariant());

        // Keep Japanese dakuten/handakuten while treating Latin accents as
        // search-insensitive. This mirrors the v3.0 browser fix that made
        // searches such as ガッチャマン and プリキュア reliable.
        string decomposed = value.Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        char previous = '\0';
        foreach (char c in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark && IsAsciiLatin(previous))
                continue;
            builder.Append(c);
            previous = c;
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool IsAsciiLatin(char c)
        => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private sealed class CatalogueStringComparer : IComparer<string>
    {
        public static readonly CatalogueStringComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            int bx = Bucket(x), by = Bucket(y);
            if (bx != by) return bx.CompareTo(by);
            return StringComparer.CurrentCultureIgnoreCase.Compare(x, y);
        }

        private static int Bucket(string value)
        {
            Rune? first = value.EnumerateRunes().FirstOrDefault();
            if (first is null || first.Value.Value == 0) return 2;
            Rune rune = first.Value;
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter)
                return 0;
            if (category == UnicodeCategory.DecimalDigitNumber) return 1;
            return 2;
        }
    }

    internal sealed class CatalogueDocument
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; set; }

        [JsonPropertyName("catalogue")]
        public string? Catalogue { get; set; }

        [JsonPropertyName("generated_at")]
        public string? GeneratedAt { get; set; }

        [JsonPropertyName("collections")]
        public List<CatalogueCollection>? Collections { get; set; }

        [JsonPropertyName("songs")]
        public List<CatalogueSong>? Songs { get; set; }
    }
}

public enum CatalogueCollectionKind
{
    Franchise,
    Series,
    Work,
    Category,
    Game,
    Artist,
    /// <summary>Songs sharing a Degausser feature, such as bundled English lyrics.</summary>
    Feature
}

/// <summary>
/// A browsable grouping of catalogue songs: a franchise, game series, game,
/// work (anime, drama, film, commercial…), theme category or artist.
/// </summary>
public sealed class CatalogueCollection
{
    private string normalizedName = string.Empty;
    private string[] normalizedOtherNames = [];

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string KindName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("original")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("media")]
    public List<string> MediaTypes { get; set; } = [];

    [JsonPropertyName("parent")]
    public int? ParentIndex { get; set; }

    [JsonIgnore]
    public CatalogueCollectionKind Kind => KindName switch
    {
        "franchise" => CatalogueCollectionKind.Franchise,
        "series" => CatalogueCollectionKind.Series,
        "category" => CatalogueCollectionKind.Category,
        "game" => CatalogueCollectionKind.Game,
        "artist" => CatalogueCollectionKind.Artist,
        "feature" => CatalogueCollectionKind.Feature,
        _ => CatalogueCollectionKind.Work
    };

    [JsonIgnore]
    public CatalogueCollection? Parent { get; internal set; }

    internal List<CatalogueCollection> ChildList { get; } = [];

    [JsonIgnore]
    public IReadOnlyList<CatalogueCollection> Children => ChildList;

    /// <summary>Distinct songs in this collection, including sub-collections.</summary>
    [JsonIgnore]
    public int SongCount { get; internal set; }

    /// <summary>Short description such as "franchise", "anime", "game" or "artist".</summary>
    [JsonIgnore]
    public string KindText => Kind switch
    {
        CatalogueCollectionKind.Work when MediaTypes.Count > 0 => MediaTypes[0],
        CatalogueCollectionKind.Work => "work",
        CatalogueCollectionKind.Category => "theme",
        CatalogueCollectionKind.Feature when MediaTypes.Count > 0 => MediaTypes[0],
        _ => KindName
    };

    public IEnumerable<CatalogueCollection> SelfAndDescendants()
    {
        yield return this;
        foreach (CatalogueCollection child in ChildList)
            foreach (CatalogueCollection descendant in child.SelfAndDescendants())
                yield return descendant;
    }

    internal void PrepareSearch()
    {
        normalizedName = BbpCatalogue.NormalizeSearchText(Name);
        normalizedOtherNames = [.. new[] { OriginalName }.Concat(Aliases)
            .Select(BbpCatalogue.NormalizeSearchText)
            .Where(x => x.Length > 0)];
    }

    internal int Score(string query)
    {
        if (normalizedName == query) return 100;
        if (normalizedName.StartsWith(query, StringComparison.Ordinal)) return 70;
        if (normalizedOtherNames.Any(x => x == query)) return 65;
        if (normalizedName.Contains(query, StringComparison.Ordinal)) return 40;
        if (normalizedOtherNames.Any(x => x.Contains(query, StringComparison.Ordinal))) return 30;
        return 0;
    }

    public override string ToString() => $"{Name} ({KindText})";
}

/// <summary>A song's membership of a collection, with its usage there (e.g. opening 11).</summary>
public sealed record CatalogueMembership(CatalogueCollection Collection, string? Usage, int? Sequence)
{
    /// <summary>Short label such as "OP 11", "ED", "Theme", "Insert" or "CM".</summary>
    public string UsageText
    {
        get
        {
            string label = Usage switch
            {
                "opening" => "OP",
                "ending" => "ED",
                "theme_song" or "theme" => "Theme",
                "insert_song" => "Insert",
                "commercial_song" => "CM",
                _ => string.Empty
            };
            return Sequence is int n && label.Length > 0 ? $"{label} {n}" : label;
        }
    }

    internal static int UsageOrder(string? usage) => usage switch
    {
        "opening" => 0,
        "ending" => 1,
        "theme_song" or "theme" => 2,
        "insert_song" => 3,
        "commercial_song" => 4,
        _ => 5
    };
}

/// <summary>A song as listed within a collection, with the membership that placed it there.</summary>
public sealed record CatalogueSongInCollection(CatalogueSong Song, CatalogueMembership Membership)
{
    /// <summary>"OP 11", or the sub-collection name when browsing a franchise, or both.</summary>
    public string DescribeIn(CatalogueCollection browsed)
    {
        string usage = Membership.UsageText;
        CatalogueCollection within = Membership.Collection;
        if (within == browsed) return usage;
        // e.g. the DS game "Rhythm Heaven" inside the franchise "Rhythm Heaven".
        string name = within.Name == browsed.Name && within.OriginalName.Length > 0 ? within.OriginalName : within.Name;
        return usage.Length == 0 ? name : $"{name} · {usage}";
    }
}

public sealed class CatalogueSong
{
    private string searchText = string.Empty;
    private string normalizedTitle = string.Empty;
    private string normalizedOriginal = string.Empty;
    private string normalizedArtists = string.Empty;

    [JsonPropertyName("id")]
    public string SongId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("original")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("artists")]
    public List<string> Artists { get; set; } = [];

    [JsonPropertyName("context")]
    public List<string> Context { get; set; } = [];

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = [];

    [JsonPropertyName("year")]
    public int? OriginalReleaseYear { get; set; }

    [JsonPropertyName("q")]
    public string SearchCorpus { get; set; } = string.Empty;

    [JsonPropertyName("arrangements")]
    public List<CatalogueArrangement> Arrangements { get; set; } = [];

    /// <summary>Raw membership rows: [collection index] or [collection index, usage, sequence].</summary>
    [JsonPropertyName("in")]
    public List<JsonElement[]> RawMemberships { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<CatalogueMembership> Memberships { get; private set; } = [];

    [JsonIgnore]
    public string ArtistText => Artists.Count == 0 ? string.Empty : string.Join(" / ", Artists);

    [JsonIgnore]
    public string ContextText => Context.Count == 0 ? string.Empty : string.Join(" · ", Context);

    [JsonIgnore]
    public int ArrangementCount => Arrangements.Count;

    [JsonIgnore]
    public int VocaloidCount => Arrangements.Count(a => a.HasVocaloidData);

    [JsonIgnore]
    public string VocaloidSummary => VocaloidCount == 0 ? string.Empty : VocaloidCount == ArrangementCount ? "All" : $"{VocaloidCount}/{ArrangementCount}";

    /// <summary>
    /// The arrangement to use when one copy of this song is wanted, among those
    /// <paramref name="isAvailable"/> accepts. Richer arrangements win: English
    /// lyrics (a bundled scorefix), then karaoke lyrics, then more parts, then
    /// native P over DX migrations, then Vocaloid data, then catalogue order.
    /// </summary>
    public CatalogueArrangement? PreferredArrangement(Func<CatalogueArrangement, bool> isAvailable)
        => Arrangements
            .Select((arrangement, order) => (arrangement, order))
            .Where(x => isAvailable(x.arrangement))
            .OrderByDescending(x => x.arrangement.HasScorefix)
            .ThenByDescending(x => x.arrangement.HasLyrics)
            .ThenByDescending(x => x.arrangement.PartCount)
            .ThenBy(x => x.arrangement.Namespace == "dx" ? 1 : 0)
            .ThenBy(x => x.arrangement.HasVocaloidData ? 0 : 1)
            .ThenBy(x => x.order)
            .Select(x => x.arrangement)
            .FirstOrDefault();

    [JsonIgnore]
    public string OriginalDisplay => BbpCatalogue.NormalizeSearchText(Title) == BbpCatalogue.NormalizeSearchText(OriginalTitle)
        ? string.Empty : OriginalTitle;

    internal void ResolveMemberships(IReadOnlyList<CatalogueCollection> collections)
    {
        var memberships = new List<CatalogueMembership>(RawMemberships.Count);
        foreach (JsonElement[] row in RawMemberships)
        {
            if (row.Length == 0 || row[0].ValueKind != JsonValueKind.Number) continue;
            int index = row[0].GetInt32();
            if (index < 0 || index >= collections.Count) continue;
            string? usage = row.Length > 1 && row[1].ValueKind == JsonValueKind.String ? row[1].GetString() : null;
            int? sequence = row.Length > 2 && row[2].ValueKind == JsonValueKind.Number ? row[2].GetInt32() : null;
            memberships.Add(new CatalogueMembership(collections[index], usage, sequence));
        }
        Memberships = memberships;
        RawMemberships = [];

        var counted = new HashSet<CatalogueCollection>();
        foreach (CatalogueMembership membership in memberships)
            for (CatalogueCollection? c = membership.Collection; c is not null && counted.Add(c); c = c.Parent)
                c.SongCount++;
    }

    /// <summary>Adds or removes a plain (no usage) membership of a collection that has no parent.</summary>
    internal void SetMembership(CatalogueCollection collection, bool member)
    {
        Memberships = [.. Memberships.Where(m => m.Collection != collection)];
        if (!member) return;
        Memberships = [.. Memberships, new CatalogueMembership(collection, null, null)];
        collection.SongCount++;
    }

    internal void PrepareSearch()
    {
        searchText = BbpCatalogue.NormalizeSearchText(SearchCorpus);
        normalizedTitle = BbpCatalogue.NormalizeSearchText(Title);
        normalizedOriginal = BbpCatalogue.NormalizeSearchText(OriginalTitle);
        normalizedArtists = BbpCatalogue.NormalizeSearchText(ArtistText);
    }

    internal int Score(IReadOnlyList<string> terms, string fullQuery)
    {
        if (terms.Count == 0) return 0;
        if (terms.Any(term => !searchText.Contains(term, StringComparison.Ordinal)))
            return -1;

        int score = 0;
        if (normalizedTitle == fullQuery) score += 120;
        if (normalizedTitle.StartsWith(fullQuery, StringComparison.Ordinal)) score += 80;
        if (normalizedTitle.Contains(fullQuery, StringComparison.Ordinal)) score += 50;
        if (normalizedArtists == fullQuery) score += 60;
        if (normalizedArtists.StartsWith(fullQuery, StringComparison.Ordinal)) score += 35;
        if (normalizedOriginal.Contains(fullQuery, StringComparison.Ordinal)) score += 30;
        foreach (string term in terms)
        {
            if (normalizedTitle.Contains(term, StringComparison.Ordinal)) score += 14;
            if (normalizedArtists.Contains(term, StringComparison.Ordinal)) score += 9;
            if (searchText.Contains(term, StringComparison.Ordinal)) score += 3;
        }
        return score;
    }
}

public sealed class CatalogueArrangement
{
    [JsonPropertyName("id")]
    public uint TitleId { get; set; }

    [JsonPropertyName("file")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("arranger")]
    public string Arranger { get; set; } = string.Empty;

    [JsonPropertyName("ns")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("dist")]
    public string DistributionType { get; set; } = string.Empty;

    [JsonPropertyName("voc")]
    public bool HasVocaloidData { get; set; }

    /// <summary>English remark, e.g. "TV Tokyo anime Gintama, insert song".</summary>
    [JsonPropertyName("rm")]
    public string Remark { get; set; } = string.Empty;

    /// <summary>Raw stats from the official corpus: [seconds, parts used, feature bits, attacks per minute].</summary>
    [JsonPropertyName("st")]
    public int[]? RawStats { get; set; }

    [JsonIgnore]
    public bool HasStats => RawStats is { Length: >= 4 };

    [JsonIgnore]
    public int Seconds => HasStats ? RawStats![0] : 0;

    [JsonIgnore]
    public int PartCount => HasStats ? RawStats![1] : 0;

    [JsonIgnore]
    public bool HasLyrics => HasStats && (RawStats![2] & 1) != 0;

    [JsonIgnore]
    public bool HasPianoPart => HasStats && (RawStats![2] & 2) != 0;

    [JsonIgnore]
    public bool HasGuitarPart => HasStats && (RawStats![2] & 4) != 0;

    [JsonIgnore]
    public bool HasDrumPart => HasStats && (RawStats![2] & 8) != 0;

    /// <summary>Note attacks per minute across all parts; a rough busyness measure.</summary>
    [JsonIgnore]
    public int AttacksPerMinute => HasStats ? RawStats![3] : 0;

    /// <summary>Degausser bundles a romaji scorefix for this arrangement.</summary>
    [JsonIgnore]
    /// <summary>Degausser has a scorefix for it: an English title, and usually English lyrics.</summary>
    public bool HasScorefix => ScorefixLibrary.Current.Has(TitleId);

    /// <summary>The scorefix has romaji lyrics (a title-only one, for an instrumental, doesn't).</summary>
    [JsonIgnore]
    public bool HasScorefixLyrics => ScorefixLibrary.Current.HasLyrics(TitleId);

    [JsonIgnore]
    public string LengthText => HasStats ? $"{Seconds / 60}:{Seconds % 60:00}" : string.Empty;

    /// <summary>e.g. "Lyrics · Piano · Guitar".</summary>
    [JsonIgnore]
    public string FeaturesText => string.Join(" · ", new[]
    {
        HasScorefixLyrics ? "English lyrics" : HasLyrics ? "Lyrics" : null,
        HasScorefix && !HasScorefixLyrics ? "English title" : null,
        HasPianoPart ? "Piano" : null,
        HasGuitarPart ? "Guitar" : null,
        HasVocaloidData ? "Vocaloid" : null
    }.Where(x => x is not null));

    [JsonIgnore]
    public string TitleIdHex => $"0x{TitleId:X8}";

    [JsonIgnore]
    public string TypeText => Namespace switch
    {
        "dx" => "DX migration",
        "p" => "P",
        _ => string.IsNullOrWhiteSpace(Namespace) ? DistributionType : Namespace
    };
}
