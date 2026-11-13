namespace Degausser.Core.Tests;

public sealed class SongImportTests
{
    private static BBPRecord SyntheticSong()
        => BBPRecord.FromBbp(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", "degausser-test-scale.bbp")));

    [Fact]
    public void AddsToFirstFreeSlotAndReturnsAPackForTheAssignedId()
    {
        JbMgr mgr = JbMgr.CreateEmpty();
        var ids = SongImport.LogicalIds(mgr);
        BBPRecord song = SyntheticSong();

        SongImport.Added added = SongImport.Add(mgr, ids, song);

        Assert.Equal(0, added.Slot);
        Assert.Equal(added.TitleId, mgr.items[0].titleID);
        Assert.Contains(added.TitleId, ids);
        Assert.Equal($"jb/gak/{added.TitleId:x8}/pack", added.PackPath);
        BBPRecord reread = BBPRecord.FromPack(added.Pack);
        Assert.Equal((int)added.TitleId, reread.gak.titleID);
    }

    [Fact]
    public void CustomSongsGetTheNextUnusedCustomId()
    {
        JbMgr mgr = JbMgr.CreateEmpty();
        var ids = SongImport.LogicalIds(mgr);
        BBPRecord song = SyntheticSong();
        Assert.Equal(0x8000u, song.mgrItem.titleID >> 16);

        uint first = SongImport.Add(mgr, ids, song).TitleId;
        uint second = SongImport.Add(mgr, ids, SyntheticSong()).TitleId;

        Assert.NotEqual(first, second);
        Assert.Equal(1, Array.FindIndex(mgr.items, i => i.titleID == second));
    }

    [Fact]
    public void OfficialSongsAlreadyInTheSaveAreRefused()
    {
        JbMgr mgr = JbMgr.CreateEmpty();
        BBPRecord song = SyntheticSong();
        song.mgrItem.titleID = 0x0000273C;
        var ids = SongImport.LogicalIds(mgr);
        SongImport.Add(mgr, ids, song);

        BBPRecord again = SyntheticSong();
        again.mgrItem.titleID = 0x0000273C;
        Assert.Throws<SongImportException>(() => SongImport.Add(mgr, ids, again));
    }

    [Fact]
    public void BundledEnglishLyricsAreOnlyAppliedWhenAskedAndMustFitTheSong()
    {
        // WEEEEK has a bundled scorefix, but the synthetic song has no matching karaoke.
        BBPRecord song = SyntheticSong();
        song.mgrItem.titleID = 0x00006C20;
        string title = song.mgrItem.title;
        Assert.NotNull(ScorefixLibrary.Current.Find(0x00006C20));

        JbMgr mgr = JbMgr.CreateEmpty();
        var ids = SongImport.LogicalIds(mgr);
        Assert.Throws<SongImportException>(() => SongImport.Add(mgr, ids, song, englishLyrics: true));
        Assert.Equal(title, song.mgrItem.title);

        SongImport.Added added = SongImport.Add(mgr, ids, song);
        Assert.False(added.EnglishLyrics);
    }

    [Fact]
    public void ManagerContainerKeepsTheCompressedLengthFooter()
    {
        JbMgr mgr = JbMgr.CreateEmpty();
        SongImport.Add(mgr, SongImport.LogicalIds(mgr), SyntheticSong());
        byte[] container = ExtDataSafeFile.Encode(mgr.ToBytes(), SongImport.ManagerContainerSize);

        Assert.Equal(SongImport.ManagerContainerSize, container.Length);
        Assert.NotEqual(0, BitConverter.ToInt32(container, container.Length - 4));
        Assert.Equal(mgr.ToBytes(), ExtDataSafeFile.Decode(container));
    }
}
