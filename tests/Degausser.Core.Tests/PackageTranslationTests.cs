namespace Degausser.Core.Tests;

public class PackageTranslationTests
{
    [Fact]
    public void HugeDeclaredEntryLengthsAreInvalidDataNotOverflow()
    {
        string path = Path.Combine(Path.GetTempPath(), "degausser-overflow-" + Guid.NewGuid().ToString("N") + ".bbpm");
        try
        {
            File.WriteAllText(path, "BBPM 1\n9223372036854775807\ta\n9223372036854775807\tb\n\n");
            Assert.Throws<InvalidDataException>(() => SimplePackage.Open(path, "BBPM"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BbpmTranslationLeavesMOFLEXAndSongPackByteIdentical()
    {
        string folder = Path.Combine(Path.GetTempPath(), "degausser-media-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string input = Path.Combine(folder, "movie.bbpm");
            string output = Path.Combine(folder, "movie.en.bbpm");
            var song = JbMgr2.Item.CreateEmpty();
            song.item.titleID = 0x12345678;
            song.item.videoID = 0x0000ABCD;
            song.item.title = "JP title";
            var movie = MvMgr.Item.CreateEmpty();
            movie.hasItem = 1;
            movie.videoID = 0x0000ABCD;
            movie.title = "JP movie title";
            byte[] moflex = [0x4C, 0x32, 0xAA, 0xBB, 0x00, 0xFF];
            byte[] pack = [1, 2, 3, 4];
            SimplePackage.Write(input, "BBPM", [
                new SimplePackage.WriteEntry(BbpmPackage.JbItemName, song.ToBytes()),
                new SimplePackage.WriteEntry(BbpmPackage.MovieItemName, movie.ToBytes()),
                new SimplePackage.WriteEntry(BbpmPackage.PackName, pack),
                new SimplePackage.WriteEntry(BbpmPackage.MovieName, moflex)]);
            var manifest = new ContentTranslation.Manifest(
                new Dictionary<string, ContentTranslation.Movie>
                {
                    ["0000ABCD"] = new("English title", "English description", "English song")
                }, null);
            ContentTranslation.ApplyPackage(input, output, manifest);
            BbpmPackage result = BbpmPackage.Open(output);
            Assert.Equal("English title", result.MovieItem.title);
            Assert.Equal("English description", result.MovieItem.description);
            Assert.Equal("English song", result.JbItem.item.title);
            Assert.Equal(moflex, result.Package.ReadEntryBytes(BbpmPackage.MovieName));
            Assert.Equal(pack, result.Package.ReadEntryBytes(BbpmPackage.PackName));
            Assert.False(result.HasAdpcm); // Old pack-only BBPM is still valid.
            Assert.Equal(SimplePackage.Open(input, "BBPM").Entries.Select(e => e.Name),
                result.Package.Entries.Select(e => e.Name));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BbpmWriterSupportsOptionalGakAdpcmAndTranslationPreservesIt(bool withAdpcm)
    {
        string folder = Path.Combine(Path.GetTempPath(), "degausser-bbpm-adpcm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string input = Path.Combine(folder, "source.bbpm");
            string translated = Path.Combine(folder, "translated.bbpm");
            string packPath = Path.Combine(folder, "pack");
            string adpcmPath = Path.Combine(folder, "adpcm");
            string moviePath = Path.Combine(folder, "moflex");
            byte[] pack = [0x01, 0x02, 0x03];
            byte[] adpcm = [0xAD, 0x50, 0x00, 0xFF, 0x12, 0x80];
            byte[] movieBytes = [0x4C, 0x32, 0x08, 0x01];
            File.WriteAllBytes(packPath, pack);
            File.WriteAllBytes(adpcmPath, adpcm);
            File.WriteAllBytes(moviePath, movieBytes);
            var jb = JbMgr2.Item.CreateEmpty();
            jb.item.titleID = 0x12345678;
            jb.item.videoID = 0x0000ABCD;
            jb.item.title = "Japanese";
            var movie = MvMgr.Item.CreateEmpty();
            movie.hasItem = 1;
            movie.videoID = 0x0000ABCD;
            movie.title = "Original";

            BbpmPackage.Write(input, jb, movie, packPath, moviePath,
                withAdpcm ? adpcmPath : null);
            BbpmPackage original = BbpmPackage.Open(input);
            Assert.Equal(withAdpcm, original.HasAdpcm);
            Assert.Equal(withAdpcm ? 5 : 4, original.Package.Entries.Count);
            Assert.Equal(pack, original.Package.ReadEntryBytes(BbpmPackage.PackName));
            Assert.Equal(movieBytes, original.Package.ReadEntryBytes(BbpmPackage.MovieName));
            if (withAdpcm)
                Assert.Equal(adpcm, original.Package.ReadEntryBytes(BbpmPackage.AdpcmName));
            else
                Assert.False(original.Package.TryGetEntry(BbpmPackage.AdpcmName, out _));

            var manifest = new ContentTranslation.Manifest(
                new Dictionary<string, ContentTranslation.Movie>
                {
                    ["0000ABCD"] = new("English title", "English description", "English song")
                }, null);
            ContentTranslation.ApplyPackage(input, translated, manifest);
            BbpmPackage result = BbpmPackage.Open(translated);
            Assert.Equal("English title", result.MovieItem.title);
            Assert.Equal(withAdpcm, result.HasAdpcm);
            Assert.Equal(original.Package.Entries.Select(e => e.Name),
                result.Package.Entries.Select(e => e.Name));
            Assert.Equal(pack, result.Package.ReadEntryBytes(BbpmPackage.PackName));
            Assert.Equal(movieBytes, result.Package.ReadEntryBytes(BbpmPackage.MovieName));
            if (withAdpcm)
                Assert.Equal(adpcm, result.Package.ReadEntryBytes(BbpmPackage.AdpcmName));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void BundledEnglishCatalogueHasFortyMoviesAndTenSourceGuardedSingers()
    {
        ContentTranslation.Manifest catalog = ContentTranslation.ReadBundledEnglish();
        Assert.Equal(40, catalog.Movies?.Count);
        Assert.Equal(10, catalog.Singers?.Count);
        Assert.All(catalog.Movies!.Values, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.ExpectedTitle));
            Assert.False(string.IsNullOrWhiteSpace(entry.ExpectedDescription));
            Assert.False(string.IsNullOrWhiteSpace(entry.ExpectedSongTitle));
            Assert.True(entry.Title!.Length <= 50);
            Assert.True(entry.Description!.Length <= 140);
        });
        Assert.All(catalog.Singers!.Values, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.ExpectedArtist));
            Assert.False(string.IsNullOrWhiteSpace(entry.ExpectedGreeting));
            Assert.True(entry.Artist!.Length <= 7);
            Assert.True(entry.Greeting!.Length <= 179);
        });
    }

    [Fact]
    public void BundledMovieTranslationMatchesJapaneseOriginalAndRejectsChangedText()
    {
        string dir = Path.Combine(Path.GetTempPath(), "degausser-english-movies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var catalog = ContentTranslation.ReadBundledEnglish();
            string path = Path.Combine(dir, "test.bbpm");
            var jb = JbMgr2.Item.CreateEmpty();
            jb.item.titleID = 0x9000284C;
            jb.item.videoID = 0x00CAC1A2;
            jb.item.title = "キミ色に染めて";
            var movie = MvMgr.Item.CreateEmpty();
            movie.hasItem = 1;
            movie.videoID = jb.item.videoID;
            movie.title = "Splatoon\nキミ色に染めて";
            movie.description = "Splatoon\nキミ色に染めて";
            SimplePackage.Write(path, "BBPM", [
                new SimplePackage.WriteEntry(BbpmPackage.JbItemName, jb.ToBytes()),
                new SimplePackage.WriteEntry(BbpmPackage.MovieItemName, movie.ToBytes()),
                new SimplePackage.WriteEntry(BbpmPackage.PackName, new byte[] { 1 }),
                new SimplePackage.WriteEntry(BbpmPackage.MovieName, new byte[] { 2 })]);
            var package = BbpmPackage.Open(path);
            Assert.True(ContentTranslation.TryTranslateMovie(package, catalog, out var translatedSong, out var translatedMovie));
            Assert.Equal("Splatoon\nInk Me Up", translatedMovie.title);
            Assert.Equal("Ink Me Up", translatedSong.item.title);
            Assert.Equal("キミ色に染めて", package.JbItem.item.title); // package remains unchanged
            // A reused ID with different source text must NOT get this translation.
            movie.title = "Another movie";
            SimplePackage.Write(path, "BBPM", [
                new SimplePackage.WriteEntry(BbpmPackage.JbItemName, jb.ToBytes()),
                new SimplePackage.WriteEntry(BbpmPackage.MovieItemName, movie.ToBytes()),
                new SimplePackage.WriteEntry(BbpmPackage.PackName, new byte[] { 1 }),
                new SimplePackage.WriteEntry(BbpmPackage.MovieName, new byte[] { 2 })]);
            Assert.False(ContentTranslation.TryTranslateMovie(BbpmPackage.Open(path), catalog,
                out _, out _));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BundledSingerTranslationMatchesGreetingAndRejectsWrongSinger()
    {
        string dir = Path.Combine(Path.GetTempPath(), "degausser-english-singer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "source.bbpv");
            var catalog = ContentTranslation.ReadBundledEnglish();
            var old = catalog.Singers!["あおきラピス"];
            var singer = SingerMgr2.Item.CreateEmpty();
            singer.item.artist = old.ExpectedArtist!;
            singer.item.greeting = old.ExpectedGreeting!;
            void WriteSinger() => SimplePackage.Write(path, "BBPV", [
                new SimplePackage.WriteEntry(BbpvPackage.SingerItemName, singer.ToBytes()),
                new SimplePackage.WriteEntry(BbpvPackage.SingerPackName, new byte[] { 1 }),
                new SimplePackage.WriteEntry(BbpvPackage.SingerThumbName, new byte[] { 2 }),
                new SimplePackage.WriteEntry("voc/db/db99.bin", new byte[] { 3 }),
                new SimplePackage.WriteEntry("voc/db/db99.ddi", new byte[] { 4 }),
                new SimplePackage.WriteEntry("voc/db/db99.ddb", new byte[] { 5 })]);
            WriteSinger();
            var package = BbpvPackage.Open(path);
            Assert.True(ContentTranslation.TryTranslateSinger(package, catalog, out var translated));
            Assert.Equal("Lapis", translated.item.artist);
            Assert.Contains("president", translated.item.greeting);
            Assert.Equal(old.ExpectedArtist, package.SingerItem.item.artist);
            singer.item.greeting = "Different greeting";
            WriteSinger();
            Assert.False(ContentTranslation.TryTranslateSinger(BbpvPackage.Open(path), catalog, out _));
        }
        finally { Directory.Delete(dir, true); }
    }

}
