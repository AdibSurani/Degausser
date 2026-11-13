namespace Degausser.Core.Tests;

// The browser can't run NFKC, so Core falls back to a table there. These compare
// the table against the real thing, which the desktop runtime has.
public sealed class TextFoldingTests
{
    [Theory]
    [InlineData("ＷＥＥＥＥＫ　ｗｅｅｅｅｋ！")]
    [InlineData("ｶﾞﾝﾀﾞﾑ ﾊﾟﾗﾀﾞｲｽ｡")]
    [InlineData("ファイナルファンタジーⅢ")]
    [InlineData("それは…‥ ①⑳ ™ №")]
    [InlineData("か\u3099らす は\u309Aん")]
    [InlineData("Pokémon Tōkyō")]
    public void TableMatchesNfkc(string text)
        => Assert.Equal(text.Normalize(System.Text.NormalizationForm.FormKC), TextFolding.CompatibilityTable(text));

    [Fact]
    public void CatalogueSearchTextIsTheSameWithTheTable()
    {
        BbpCatalogue catalogue = BbpCatalogue.LoadEmbedded();
        var texts = catalogue.Songs.SelectMany(s => new[] { s.Title, s.OriginalTitle, s.ArtistText, s.SearchCorpus })
            .Concat(catalogue.Collections.SelectMany(c => new[] { c.Name, c.OriginalName }))
            .ToList();
        var different = texts
            .Where(t => BbpCatalogue.NormalizeSearchText(t, true) != BbpCatalogue.NormalizeSearchText(t, false))
            .ToList();
        Assert.True(different.Count <= texts.Count / 1000, $"{different.Count} of {texts.Count} differ, e.g. {string.Join(" | ", different.Take(5))}");
    }
}
