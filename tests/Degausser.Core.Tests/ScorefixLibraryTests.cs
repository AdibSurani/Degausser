using System.Runtime.CompilerServices;

namespace Degausser.Core.Tests;

internal static class ScorefixFolder
{
    public static string Root { get; } = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scorefixes"));

    /// <summary>The app loads its scorefix folder at startup; the tests load the repo's corpus once, before any test runs.</summary>
    [ModuleInitializer]
    internal static void LoadCorpus() => ScorefixLibrary.Current = ScorefixLibrary.FromDirectory(Root);
}

public sealed class ScorefixLibraryTests
{
    [Fact]
    public void EveryScorefixFileIsLoadedUnderItsId()
    {
        int files = Directory.GetFiles(ScorefixFolder.Root, "*.txt", SearchOption.AllDirectories)
            .Count(f => !Path.GetFileName(f).Equals("README.txt", StringComparison.OrdinalIgnoreCase));

        ScorefixLibrary library = ScorefixLibrary.FromDirectory(ScorefixFolder.Root);
        Assert.Empty(library.Problems);
        Assert.Equal(files, library.Count);
        Assert.Equal("STARLIGHT", ScorefixDocument.Parse(library.Find(0x80010000)!).Title); // built-in, ID from file name
        Assert.Equal("Senbonzakura", ScorefixDocument.Parse(library.Find(0x900027BA)!).Title); // movie song
        Assert.False(library.Has(0xFFFFFFFF));
        Assert.True(library.HasLyrics(0x80010000));
        Assert.True(library.Has(0x00007A95)); // Rhythm Rally: an instrumental, so a title-only scorefix
        Assert.False(library.HasLyrics(0x00007A95));
    }

    [Fact]
    public void ATitleOnlyScorefixIsAnEnglishTitleNotEnglishLyricsButStillListed()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        CatalogueArrangement rally = catalogue.FindArrangement(0x00007A95)!;
        Assert.True(rally.HasScorefix);
        Assert.False(rally.HasScorefixLyrics);
        Assert.Contains("English title", rally.FeaturesText);
        Assert.DoesNotContain("English lyrics", rally.FeaturesText);
        Assert.Contains(catalogue.Scorefixes, catalogue.FindSong(0x00007A95)!.Memberships.Select(m => m.Collection));
    }

    [Fact]
    public void NoFolderMeansNoScorefixes()
    {
        Assert.Equal(0, ScorefixLibrary.FromDirectory(null).Count);
        Assert.Equal(0, ScorefixLibrary.FromDirectory("").Count);
        Assert.Equal(0, ScorefixLibrary.FromDirectory(Path.Combine(ScorefixFolder.Root, "does-not-exist")).Count);
    }

    [Fact]
    public void ABadFileIsSkippedAndReportedNotFatal()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dg-scorefix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "shared"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "00001234.txt"), "Title\n---\nlyrics");      // ID from the file name
            File.WriteAllText(Path.Combine(dir, "shared", "x.TXT"), "00005678\n---\nTitle\n---\nlyrics"); // ID header, upper-case extension
            File.WriteAllText(Path.Combine(dir, "junk.txt"), "no separator here");
            File.WriteAllText(Path.Combine(dir, "noid.txt"), "Title\n---\nlyrics");
            File.WriteAllText(Path.Combine(dir, "README.txt"), "Not a scorefix");
            File.WriteAllText(Path.Combine(dir, "notes.md"), "ignored");

            ScorefixLibrary library = ScorefixLibrary.FromDirectory(dir);

            Assert.Equal([0x1234u, 0x5678u], library.All.Keys.Order());
            Assert.Equal(2, library.Problems.Count);
            Assert.Contains(library.Problems, p => p.StartsWith("junk.txt", StringComparison.Ordinal));
            Assert.Contains(library.Problems, p => p.StartsWith("noid.txt", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TwoFilesForOneIdAreReportedAndTheLaterWins()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dg-scorefix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "00000001\n---\nFirst\n---\nl");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "00000001\n---\nSecond\n---\nl");

            ScorefixLibrary library = ScorefixLibrary.FromDirectory(dir);

            Assert.Equal("Second", ScorefixDocument.Parse(library.Find(1)!).Title);
            Assert.Contains("both for 00000001", Assert.Single(library.Problems));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CatalogueOffersScorefixesOnlyWhileTheyAreLoaded()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        ScorefixLibrary loaded = ScorefixLibrary.Current;
        Assert.Contains(catalogue.Scorefixes, catalogue.FeaturedCollections);
        try
        {
            ScorefixLibrary.Current = ScorefixLibrary.Empty;
            catalogue.RefreshScorefixes();
            Assert.Empty(catalogue.FeaturedCollections);
            Assert.DoesNotContain(catalogue.Scorefixes, catalogue.Collections);
            Assert.Empty(catalogue.SearchCollections("scorefix"));
            Assert.False(catalogue.FindArrangement(0x00006C20)!.HasScorefix);
        }
        finally
        {
            ScorefixLibrary.Current = loaded;
        }
        catalogue.RefreshScorefixes();
        Assert.Contains(catalogue.Scorefixes, catalogue.Collections);
        Assert.True(catalogue.Scorefixes.SongCount >= 100);
    }
}
