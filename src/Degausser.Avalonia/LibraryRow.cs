namespace Degausser;

/// <summary>
/// One row of the Library pane: a local file, an official arrangement, or both.
/// A local copy shows its own file metadata for the title and author; the catalogue
/// adds the English title, artist and context. An official arrangement with no local
/// copy is listed only when the whole catalogue is shown, and then has only the
/// catalogue's data.
/// </summary>
internal sealed class LibraryRow
{
    public LibrarySong? File { get; }
    public CatalogueSong? Song { get; }
    public CatalogueArrangement? Arrangement { get; }

    /// <summary>The open saves hold this arrangement.</summary>
    public bool InSave { get; }

    /// <summary>The title in the file (what the game shows); for an official arrangement you don't have, the catalogue's original title.</summary>
    public string Title { get; }

    /// <summary>The catalogue's English title, when it differs from <see cref="Title"/>.</summary>
    public string EnglishTitle { get; }

    /// <summary>Default order: the English title if there is one, so Japanese and English titles interleave alphabetically.</summary>
    public string SortTitle { get; }

    public string Artist => Song?.ArtistText ?? string.Empty;
    public string Context { get; }
    /// <summary>The author named in the file (the catalogue's arranger for a song you don't have). The game calls this Author.</summary>
    public string Author { get; }
    public string Format => File?.Format ?? string.Empty;
    public int Parts => File?.Instruments ?? Arrangement?.PartCount ?? 0;
    public string Features { get; }
    public string Filename => File?.Filename ?? string.Empty;

    public bool IsLocal => File is not null;
    public bool CanPreview => File?.CanPreviewSong ?? InSave;

    /// <summary>Identifies the row across refreshes, so a selection survives a new search.</summary>
    public string Key => File?.FullPath ?? $"catalogue:{Arrangement?.TitleId:X8}";

    private static string Fold(string text)
        => text.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToUpperInvariant();

    private const string DxMigrationPrefix = "DXいしょく@";

    /// <summary>"ＤＸいしょく＠かずき" (migrated from DX by かずき) reads as "かずき (DX)", which fits the column.</summary>
    private static string ShortAuthor(string author)
    {
        string folded = author.Normalize(System.Text.NormalizationForm.FormKC);
        return folded.StartsWith(DxMigrationPrefix, StringComparison.Ordinal) && folded.Length > DxMigrationPrefix.Length
            ? $"{folded[DxMigrationPrefix.Length..]} (DX)"
            : author;
    }

    public LibraryRow(LibrarySong? file, CatalogueSong? song, CatalogueArrangement? arrangement, bool inSave, string? context = null)
    {
        File = file;
        Song = song;
        Arrangement = arrangement;
        InSave = inSave;

        string catalogueEnglish = song?.Title ?? string.Empty;
        Title = file?.Title is { Length: > 0 } fileTitle ? fileTitle
            : song?.OriginalTitle is { Length: > 0 } original ? original
            : catalogueEnglish;
        EnglishTitle = Fold(catalogueEnglish) == Fold(Title) ? string.Empty : catalogueEnglish;
        SortTitle = catalogueEnglish.Length > 0 ? catalogueEnglish : Title;
        Context = context ?? song?.ContextText ?? string.Empty;
        Author = ShortAuthor(file?.Author is { Length: > 0 } author ? author : arrangement?.Arranger ?? string.Empty);

        bool lyrics = file?.HasKaraoke ?? arrangement?.HasLyrics ?? false;
        bool piano = file?.HasPiano ?? arrangement?.HasPianoPart ?? false;
        bool guitar = file?.HasGuitar ?? arrangement?.HasGuitarPart ?? false;
        bool english = arrangement?.HasScorefixLyrics ?? false;
        bool englishTitle = (arrangement?.HasScorefix ?? false) && !english;
        Features = string.Join(" · ", new[]
        {
            english ? "Lyrics (EN)" : lyrics ? "Lyrics" : null,
            englishTitle ? "Title (EN)" : null,
            piano ? "Piano" : null,
            guitar ? "Guitar" : null,
            arrangement?.HasVocaloidData == true ? "Vocaloid" : null
        }.Where(x => x is not null));
    }
}
