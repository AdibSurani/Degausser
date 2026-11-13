namespace Degausser.Core.Tests;

public class ScorefixTests
{
    [Fact]
    public void ParsesHistoricalScorefixWithIdAndMultilineTitle()
    {
        const string text = "800229C8\n――――――――――――\nA Long \nTitle\n――――――――――――\nromaji\nlyrics";

        ScorefixDocument doc = ScorefixDocument.Parse(text);

        Assert.Equal(0x800229C8u, doc.TitleId);
        Assert.Equal("A Long \nTitle", doc.Title);
        Assert.Equal("A Long Title", doc.FlatTitle);
        Assert.Equal("romaji\nlyrics", doc.Lyrics);
    }

    [Fact]
    public void ParsesBuiltinScorefixWithoutId()
    {
        const string text = "Eine kleine \nNachtmusik\n――――――――――――\n";

        ScorefixDocument doc = ScorefixDocument.Parse(text);

        Assert.Null(doc.TitleId);
        Assert.Equal("Eine kleine \nNachtmusik", doc.Title);
        Assert.Equal("Eine kleine Nachtmusik", doc.FlatTitle);
        Assert.Empty(doc.Lyrics);
    }

    [Fact]
    public void RejectsJbMgrTitlesOverFiftyCharacters()
    {
        string text = new string('A', 51) + "\n――――――――――――\n";
        FormatException error = Assert.Throws<FormatException>(() => ScorefixDocument.Parse(text));
        Assert.Contains("JbMgr", error.Message);
    }

    [Fact]
    public void TemplateIsAParseableScorefixWithTheSongsCurrentTitleAndLyrics()
    {
        BBPRecord song = BBPRecord.FromBbp(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", "degausser-test-scale.bbp")));
        song.gak.title = "とある\nテスト";
        song.gak.karaokeLyrics = "あいう　えお  \nかきく\n\nさしす";
        song.gak.karaokeTimer = Enumerable.Repeat((short)0x3FFF, 3000).ToArray();
        song.gak.karaokeTimer[0] = unchecked((short)0x8000);   // a timed first character: the song has karaoke

        // A custom song's ID changes on import, so the template leaves it out.
        ScorefixDocument custom = ScorefixDocument.Parse(Scorefix.Template(song));
        Assert.Null(custom.TitleId);
        Assert.Equal("とある\nテスト", custom.Title);

        var item = song.mgrItem;
        item.titleID = 0x00001234;
        song.mgrItem = item;
        ScorefixDocument official = ScorefixDocument.Parse(Scorefix.Template(song));
        Assert.Equal(0x1234u, official.TitleId);
        Assert.Equal("とある\nテスト", official.Title);
        Assert.Equal("あいう　えお\nかきく\n\nさしす", official.Lyrics);
    }

    private static BBPRecord CustomSongWithLyrics(string lyrics)
    {
        BBPRecord song = BBPRecord.FromBbp(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", "degausser-test-scale.bbp")));
        song.gak.title = "とある\nテスト";
        song.gak.karaokeLyrics = lyrics;
        song.gak.karaokeTimer = Enumerable.Repeat((short)0x3FFF, 3000).ToArray();
        song.gak.karaokeTimer[0] = unchecked((short)0x8000);
        return song;
    }

    [Fact]
    public void FingerprintIgnoresWhitespaceAndWidthButNotWording()
    {
        string? a = Scorefix.Fingerprint("あいう　えお\nかきく\n");
        Assert.Equal(Scorefix.FingerprintLength, a!.Length);
        Assert.Equal(a, Scorefix.Fingerprint("あいう えお  かきく"));
        Assert.Equal(Scorefix.Fingerprint("ＡＢＣ"), Scorefix.Fingerprint("ABC"));
        Assert.NotEqual(a, Scorefix.Fingerprint("あいう　えお\nかきけ\n"));
        Assert.Null(Scorefix.Fingerprint(""));
        Assert.Null(Scorefix.Fingerprint(" \n\0"));
    }

    [Fact]
    public void CustomSongTemplateCarriesTheLyricsFingerprintAndRoundTrips()
    {
        BBPRecord song = CustomSongWithLyrics("あいう\nえお\n");
        ScorefixDocument doc = ScorefixDocument.Parse(Scorefix.Template(song));
        Assert.Null(doc.TitleId);
        Assert.Equal(Scorefix.Fingerprint(song), doc.Fingerprint);
    }

    [Fact]
    public void LibraryFindsACustomSongByItsLyricsNotItsId()
    {
        BBPRecord song = CustomSongWithLyrics("あいう\nえお\n");
        string fingerprint = Scorefix.Fingerprint(song)!;
        string folder = Path.Combine(Path.GetTempPath(), "degausser-fp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "mine.txt"), $"lyrics:{fingerprint}\n――――――――――――\nMy Song\n――――――――――――\n");
            // A scorefix for an official ID that happens to equal this custom song's temporary ID must not match it.
            File.WriteAllText(Path.Combine(folder, "other.txt"), $"{song.mgrItem.titleID:X8}\n――――――――――――\nWrong Song\n――――――――――――\n");
            ScorefixLibrary library = ScorefixLibrary.FromDirectory(folder);

            Assert.Equal(2, library.Count);
            Assert.Empty(library.Problems);
            string found = library.Find(song)!;
            Assert.StartsWith("lyrics:", found);

            // A different custom song with the same temporary ID but other lyrics gets nothing.
            Assert.Null(library.Find(CustomSongWithLyrics("まみむ\nめも\n")));

            ScorefixDocument applied = Scorefix.Apply(song, found);
            Assert.Equal("My Song", applied.Title);
            Assert.Equal("My Song", song.mgrItem.title);
            Assert.Equal("my song", song.mgrItem.titleSimple); // lowercase, so the menu files it under M

            // Applying it to different lyrics is refused before anything changes.
            BBPRecord different = CustomSongWithLyrics("まみむ\nめも\n");
            Assert.Throws<InvalidDataException>(() => Scorefix.Apply(different, found));
            Assert.Equal("とある\nテスト", different.gak.title);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void OfficialSongsAreStillFoundById()
    {
        BBPRecord song = CustomSongWithLyrics("あいう\n");
        var item = song.mgrItem;
        item.titleID = 0x00001234;
        song.mgrItem = item;
        string folder = Path.Combine(Path.GetTempPath(), "degausser-fp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.txt"), "00001234\n――――――――――――\nOfficial\n――――――――――――\n");
            Assert.NotNull(ScorefixLibrary.FromDirectory(folder).Find(song));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void TheManagerTitleKeepsTheLineBreaksAndTheSortTitleIsFlat()
    {
        BBPRecord song = CustomSongWithLyrics("あいう\n");
        Scorefix.Apply(song, "Bowser's Bullet \nBill Brigade\n――――――――――――\n");
        Assert.Equal("Bowser's Bullet \nBill Brigade", song.mgrItem.title); // the top-screen card wraps here
        Assert.Equal("bowser's bullet bill brigade", song.mgrItem.titleSimple);
        Assert.Equal("Bowser's Bullet \nBill Brigade", song.gak.title);
    }

    [Theory]
    [InlineData("Bowser's Bullet Bill Brigade", "bowser's bullet bill brigade")]
    [InlineData("ＳＴＡＲＴ", "start")]
    [InlineData("ＨＥＬＬＯ，星を数えて", "hello,星を数えて")]
    [InlineData("101 Kaime \nno Noroi", "101 kaime no noroi")]
    [InlineData("きみの　うた", "きみの うた")]
    public void SimpleTitleIsOneLowercaseLineWithHalfWidthLatin(string title, string expected)
        => Assert.Equal(expected, JbMgr.Item.SimpleTitle(title));
}
