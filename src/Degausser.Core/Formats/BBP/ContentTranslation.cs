using System.Text.Json;

namespace Degausser;

/// <summary>
/// Validated metadata-only English overrides for BBPM/BBPV. The package media,
/// MOFLEX movie, optional GAK adpcm sidecar, song pack, thumbnail and singer
/// database are carried forward byte-for-byte. Unsupported/free-form fields never get rewritten.
/// </summary>
public static class ContentTranslation
{
    public sealed record Manifest(
        Dictionary<string, Movie>? Movies,
        Dictionary<string, Singer>? Singers);

    public sealed record Movie(string? Title = null, string? Description = null,
        string? SongTitle = null, string? SongTitleSimple = null, string? Author = null,
        string? ExpectedTitle = null, string? ExpectedDescription = null,
        string? ExpectedSongTitle = null);

    public sealed record Singer(string? Artist = null, string? Greeting = null,
        string? ExpectedArtist = null, string? ExpectedGreeting = null);

    public static Manifest ReadManifest(string path)
    {
        var manifest = JsonSerializer.Deserialize(File.ReadAllText(path), TranslationJsonContext.Default.Manifest);
        return manifest ?? throw new InvalidDataException("Translation JSON is empty.");
    }

    /// <summary>Bundled user-editable English translations for 40 movies and 10 singers.
    /// They apply only when explicitly requested, with Japanese-source guards.</summary>
    public static Manifest ReadBundledEnglish()
    {
        using Stream stream = typeof(ContentTranslation).Assembly.GetManifestResourceStream(
            "Degausser.ContentTranslations.EnglishV1")
            ?? throw new InvalidDataException("Embedded English content translation catalogue is missing.");
        return JsonSerializer.Deserialize(stream, TranslationJsonContext.Default.Manifest)
            ?? throw new InvalidDataException("Embedded English catalogue is empty.");
    }

    /// <summary>Return true only for an exact match to the original Japanese metadata.
    /// A foreign/reused video ID will never receive a mismatched translation.</summary>
    public static bool TryTranslateMovie(BbpmPackage package, Manifest manifest,
        out JbMgr2.Item song, out MvMgr.Item movie)
    {
        song = package.JbItem;
        movie = package.MovieItem;
        return TryTranslateMovie(manifest, package.VideoId, package.SongId, ref song, ref movie);
    }

    public static bool HasMovie(Manifest manifest, uint videoId, uint songId)
        => Match(manifest.Movies, videoId, songId) is not null;

    /// <summary>Same guards as the package overload, for items already in a save.</summary>
    public static bool TryTranslateMovie(Manifest manifest, uint videoId, uint songId,
        ref JbMgr2.Item song, ref MvMgr.Item movie)
    {
        Movie? patch = Match(manifest.Movies, videoId, songId);
        if (patch is null) return false;
        if ((patch.ExpectedTitle is not null && movie.title != patch.ExpectedTitle)
            || (patch.ExpectedDescription is not null && movie.description != patch.ExpectedDescription)
            || (patch.ExpectedSongTitle is not null && song.item.title != patch.ExpectedSongTitle))
            return false;
        if (patch.Title is not null) movie.title = Checked(patch.Title, 50, "movie title");
        if (patch.Description is not null) movie.description = Checked(patch.Description, 140, "movie description");
        if (patch.SongTitle is not null) song.item.title = Checked(patch.SongTitle, 50, "song title");
        if (patch.SongTitleSimple is not null) song.item.titleSimple = Checked(patch.SongTitleSimple, 50, "short song title");
        if (patch.Author is not null) song.item.author = Checked(patch.Author, 17, "author");
        return true;
    }

    /// <summary>
    /// The title the game shows while a movie song plays (and the karaoke) come from the
    /// pack, not from the managers, so they have to be translated together with them.
    /// A song with a bundled scorefix gets its romanised title and lyrics (which also set
    /// the manager titles); any other song just gets the pack title of the translated item.
    /// A scorefix that doesn't fit this copy of the song is skipped, not fatal.
    /// </summary>
    public static byte[] LocalizeMoviePack(byte[] pack, ref JbMgr2.Item song)
    {
        BBPRecord record = BBPRecord.FromPack(pack);
        record.mgrItem = song.item;
        bool scorefixed = false;
        if (ScorefixLibrary.Current.Find(song.item.titleID) is string scorefix)
        {
            try
            {
                Scorefix.Apply(record, scorefix);
                scorefixed = true;
            }
            catch (Exception exception) when (exception is InvalidDataException or FormatException)
            {
            }
        }
        if (!scorefixed)
            record.gak.title = (song.item.title ?? string.Empty).Replace("\n", string.Empty);
        song.item = record.mgrItem;
        return PlayableVideoPack.Is(pack) ? PlayableVideoPack.WithScoreText(pack, record.gak) : record.Recompile();
    }

    public static bool TryTranslateSinger(BbpvPackage package, Manifest manifest,
        out SingerMgr2.Item singer)
    {
        singer = package.SingerItem;
        Singer? patch = null;
        if (manifest.Singers is { } singers)
        {
            singers.TryGetValue(package.Artist, out patch);
            if (patch is null) singers.TryGetValue("*", out patch);
        }
        if (patch is null) return false;
        if ((patch.ExpectedArtist is not null && singer.item.artist != patch.ExpectedArtist)
            || (patch.ExpectedGreeting is not null && singer.item.greeting != patch.ExpectedGreeting))
            return false;
        if (patch.Artist is not null) singer.item.artist = Checked(patch.Artist, 7, "artist");
        if (patch.Greeting is not null) singer.item.greeting = Checked(patch.Greeting, 179, "greeting");
        return true;
    }

    /// <summary>Standalone packages are re-created with exactly their original
    /// entry list/order, replacing only known metadata items. Preserve opaque
    /// payloads via streaming, rather than decoding/re-encoding MOFLEX.</summary>
    public static void ApplyPackage(string inputPath, string outputPath, Manifest manifest)
    {
        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a separate output path to avoid overwriting the source package.");
        string extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (extension == ".bbpm")
        {
            BbpmPackage package = BbpmPackage.Open(inputPath);
            if (!TryTranslateMovie(package, manifest, out var jb, out var mv))
                throw new KeyNotFoundException($"No matching original-Japanese movie translation for {package.VideoId:X8}.");
            WriteReplacedEntries(package.Package, outputPath, new Dictionary<string, byte[]>
            {
                [BbpmPackage.JbItemName] = jb.ToBytes(),
                [BbpmPackage.MovieItemName] = mv.ToBytes()
            });
        }
        else if (extension == ".bbpv")
        {
            BbpvPackage package = BbpvPackage.Open(inputPath);
            if (!TryTranslateSinger(package, manifest, out var singer))
                throw new KeyNotFoundException($"No matching original-Japanese singer translation for '{package.Artist}'.");
            WriteReplacedEntries(package.Package, outputPath, new Dictionary<string, byte[]>
            {
                [BbpvPackage.SingerItemName] = singer.ToBytes()
            });
        }
        else throw new InvalidDataException("Expected a .bbpm or .bbpv source file.");
    }

    private static Movie? Match(Dictionary<string, Movie>? entries, params uint[] ids)
    {
        if (entries is null) return null;
        foreach (uint id in ids)
        {
            if (entries.TryGetValue(id.ToString("X8"), out Movie? patch)) return patch;
            if (entries.TryGetValue(id.ToString(), out patch)) return patch;
        }
        return entries.TryGetValue("*", out Movie? fallback) ? fallback : null;
    }

    private static string Checked(string text, int maxUtf16Units, string field)
    {
        if (text.Contains('\0') || text.Length > maxUtf16Units)
            throw new InvalidDataException($"{field} has {text.Length} UTF-16 code units; maximum is {maxUtf16Units}, without NUL.");
        return text;
    }

    private static void WriteReplacedEntries(SimplePackage source, string outputPath,
        IReadOnlyDictionary<string, byte[]> replacements)
    {
        // Stream all unchanged original entries to avoid loading movies/voice DBs
        // into RAM. Old BBPM/BBPV readers see the exact same entry names/order.
        var tempPaths = new List<string>();
        try
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "degausser-translation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            tempPaths.Add(tempDirectory);
            var entries = new List<SimplePackage.WriteEntry>();
            int index = 0;
            foreach (var entry in source.Entries)
            {
                if (replacements.TryGetValue(entry.Name, out byte[]? data))
                {
                    if (data.LongLength != entry.Length)
                        throw new InvalidDataException($"Translation changed fixed metadata size of {entry.Name}.");
                    entries.Add(new SimplePackage.WriteEntry(entry.Name, data));
                }
                else
                {
                    string temp = Path.Combine(tempDirectory, $"{index++:D3}.payload");
                    source.CopyEntryTo(entry.Name, temp);
                    entries.Add(new SimplePackage.WriteEntry(entry.Name, temp));
                }
            }
            SimplePackage.Write(outputPath, source.Magic, entries);
        }
        finally
        {
            foreach (string path in tempPaths)
                try { Directory.Delete(path, true); } catch { }
        }
    }
}
