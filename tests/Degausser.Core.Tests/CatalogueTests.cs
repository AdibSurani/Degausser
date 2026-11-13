namespace Degausser.Core.Tests;

public sealed class CatalogueTests
{
    [Fact]
    public void EmbeddedCatalogueHasExpectedV3Counts()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        Assert.Equal("3.0", catalogue.CatalogueVersion);
        Assert.Equal(7952, catalogue.Songs.Count);
        Assert.Equal(9722, catalogue.ArrangementCount);
    }

    [Theory]
    [InlineData("Koisuru Fortune Cookie", "Koisuru Fortune Cookie")]
    [InlineData("恋するフォーチュンクッキー", "Koisuru Fortune Cookie")]
    [InlineData("ガッチャマン", "Gatchaman no Uta")]
    [InlineData("プリキュア", "PreCure")]
    public void CatalogueSearchFindsEnglishJapaneseAndDakutenQueries(string query, string expectedTitleFragment)
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        IReadOnlyList<CatalogueSong> hits = catalogue.Search(query, 50);
        Assert.Contains(hits, hit => hit.Title.Contains(expectedTitleFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ArrangementLookupUsesRealTitleIds()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        CatalogueSong song = Assert.Single(catalogue.Search("Koisuru Fortune Cookie", 1));
        CatalogueArrangement arrangement = Assert.Single(song.Arrangements);
        Assert.Equal(0x0000273Cu, arrangement.TitleId);
        Assert.Same(arrangement, catalogue.FindArrangement(arrangement.TitleId));
        Assert.Same(song, catalogue.FindSong(arrangement.TitleId));
        Assert.Null(catalogue.FindSong(0xFFFFFFFEu));
    }

    [Fact]
    public void FranchiseBrowsingIncludesItsGamesWithEnglishSongTitles()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        CatalogueCollection rhythmHeaven = catalogue.SearchCollections("rhythm heaven")[0];

        Assert.Equal(CatalogueCollectionKind.Franchise, rhythmHeaven.Kind);
        Assert.Contains(rhythmHeaven.Children, c => c.Name == "Rhythm Heaven Fever");
        IReadOnlyList<CatalogueSongInCollection> songs = catalogue.SongsIn(rhythmHeaven);
        Assert.Equal(rhythmHeaven.SongCount, songs.Count);
        Assert.Contains(songs, s => s.Song.Title == "Munchy Monk Theme");
    }

    [Fact]
    public void WorkBrowsingOrdersSongsByOpeningAndEndingNumber()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        CatalogueCollection bleach = catalogue.SearchCollections("bleach")[0];
        IReadOnlyList<CatalogueSongInCollection> songs = catalogue.SongsIn(bleach);

        Assert.Equal("BLEACH", bleach.Name);
        Assert.Equal("anime", bleach.KindText);
        Assert.Equal("*~Asterisk~", songs[0].Song.Title);
        Assert.Equal("OP 1", songs[0].Membership.UsageText);
        CatalogueSongInCollection animarossa = Assert.Single(songs, s => s.Song.Title == "Animarossa");
        Assert.Equal("OP 11", animarossa.DescribeIn(bleach));
    }

    [Theory]
    [InlineData("リズム天国", "Rhythm Tengoku")]
    [InlineData("ゼルダの伝説 風のタクト", "The Legend of Zelda: The Wind Waker")]
    public void CollectionsAreFoundByTheirOriginalJapaneseNames(string query, string expected)
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        Assert.Contains(catalogue.SearchCollections(query, 10), c => c.Name == expected);
    }

    [Fact]
    public void CollectionHierarchyIsAcyclicAndCountsContainTheirChildren()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        Assert.True(catalogue.Collections.Count > 1000);
        foreach (CatalogueCollection collection in catalogue.Collections)
        {
            var seen = new HashSet<CatalogueCollection>();
            for (CatalogueCollection? c = collection; c is not null; c = c.Parent)
                Assert.True(seen.Add(c), $"Cycle through {collection}");
            Assert.All(collection.Children, child => Assert.True(child.SongCount <= collection.SongCount));
            Assert.True(collection.SongCount > 0, $"{collection} has no songs");
        }
    }

    [Fact]
    public void EveryArrangementHasCorpusStats()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        var arrangements = catalogue.Songs.SelectMany(s => s.Arrangements).ToList();
        Assert.All(arrangements, a => Assert.True(a.HasStats, a.TitleIdHex));
        Assert.All(arrangements, a => Assert.InRange(a.PartCount, 1, 10));
        Assert.True(arrangements.Count(a => a.HasLyrics) > 5000);
    }

    [Fact]
    public void PreferredArrangementIsTheRichestAvailableCopy()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        CatalogueSong song = catalogue.Songs.First(s =>
            s.Arrangements.Any(a => a.HasLyrics) && s.Arrangements.Any(a => !a.HasLyrics));

        Assert.True(song.PreferredArrangement(_ => true)!.HasLyrics);
        CatalogueArrangement plain = song.Arrangements.First(a => !a.HasLyrics);
        Assert.Same(plain, song.PreferredArrangement(a => a == plain));
        Assert.Null(song.PreferredArrangement(_ => false));
    }

    [Fact]
    public void SongsWithScorefixesFormAFeaturedCollection()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        CatalogueCollection english = catalogue.Scorefixes;

        Assert.Contains(english, catalogue.FeaturedCollections);
        Assert.Same(english, catalogue.SearchCollections("scorefix").First());
        CatalogueArrangement weeeek = catalogue.FindArrangement(0x00006C20)!;
        Assert.True(weeeek.HasScorefix);
        Assert.True(weeeek.HasScorefixLyrics);
        Assert.StartsWith("English lyrics", weeeek.FeaturesText);

        IReadOnlyList<CatalogueSongInCollection> songs = catalogue.SongsIn(english);
        Assert.Equal(english.SongCount, songs.Count);
        Assert.True(songs.Count >= 100);
        Assert.All(songs, x => Assert.True(x.Song.PreferredArrangement(_ => true)!.HasScorefix));
    }
}
