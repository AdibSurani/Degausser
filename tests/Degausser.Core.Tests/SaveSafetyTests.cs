namespace Degausser.Core.Tests;

public sealed class SaveSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "degausser-safety-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(root, "save");
    private string Backups => Path.Combine(root, "backups");

    public SaveSafetyTests()
    {
        foreach (string file in new[] { "jb/mgr.bin", "jb/mgr_.bin", "jb/mgr2.bin", "mv/mgr.bin", "singer/mgr.bin" })
        {
            string path = Path.Combine(Save, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "original " + file);
        }
        Directory.CreateDirectory(Path.Combine(Save, "jb", "gak", "80000001"));
        File.WriteAllText(Path.Combine(Save, "jb", "gak", "80000001", "pack"), "song data");
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public void FindManagerFileAcceptsTheSaveJbOrAParentWithAPFolder()
    {
        string expected = Path.Combine(Save, "jb", "mgr.bin");
        Assert.Equal(expected, ExtDataWorkspace.FindManagerFile(Save));
        Assert.Equal(expected, ExtDataWorkspace.FindManagerFile(Path.Combine(Save, "jb")));

        string card = Path.Combine(root, "card");
        string patched = Path.Combine(card, "P", "jb", "mgr.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(patched)!);
        File.WriteAllText(patched, "x");
        Assert.Equal(patched, ExtDataWorkspace.FindManagerFile(card));

        Assert.Null(ExtDataWorkspace.FindManagerFile(Path.Combine(Save, "mv")));
        Assert.Null(ExtDataWorkspace.FindManagerFile(""));
    }

    [Fact]
    public void SnapshotCopiesManagersButNotPayloads()
    {
        string snapshot = ExtDataBackup.Create(Save, Backups);

        Assert.True(File.Exists(Path.Combine(snapshot, "managers", "jb", "mgr_.bin")));
        Assert.True(File.Exists(Path.Combine(snapshot, "managers", "mv", "mgr.bin")));
        Assert.False(Directory.Exists(Path.Combine(snapshot, "managers", "jb", "gak")));
    }

    [Fact]
    public void UnchangedManagersDoNotMakeANewSnapshot()
    {
        string first = ExtDataBackup.Create(Save, Backups);
        string second = ExtDataBackup.Create(Save, Backups);
        File.WriteAllText(Path.Combine(Save, "jb", "mgr.bin"), "changed");
        string third = ExtDataBackup.Create(Save, Backups);

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
        Assert.Equal(2, ExtDataBackup.List(Backups).Count);
    }

    [Fact]
    public void RestoreBringsBackManagersAndRemovedPayloadsAndCanBeUndone()
    {
        string snapshot = ExtDataBackup.Create(Save, Backups);
        string gak = Path.Combine(Save, "jb", "gak", "80000001");
        ExtDataBackup.Preserve(snapshot, Save, gak);
        Directory.Delete(gak, true);
        File.WriteAllText(Path.Combine(Save, "jb", "mgr.bin"), "damaged");

        string safety = ExtDataBackup.Restore(snapshot, Save);

        Assert.Equal("original jb/mgr.bin", File.ReadAllText(Path.Combine(Save, "jb", "mgr.bin")));
        Assert.Equal("song data", File.ReadAllText(Path.Combine(gak, "pack")));
        // The state before the restore is itself a restore point.
        ExtDataBackup.Restore(safety, Save);
        Assert.Equal("damaged", File.ReadAllText(Path.Combine(Save, "jb", "mgr.bin")));
    }

    [Fact]
    public void OnlyTheNewestSnapshotsAreKept()
    {
        for (int i = 0; i < ExtDataBackup.MaxSnapshots + 5; i++)
        {
            File.WriteAllText(Path.Combine(Save, "jb", "mgr.bin"), "version " + i);
            ExtDataBackup.Create(Save, Backups);
        }

        Assert.Equal(ExtDataBackup.MaxSnapshots, ExtDataBackup.List(Backups).Count);
    }

    [Fact]
    public void FailedPayloadWriteTakesTheSongBackOutOfTheManager()
    {
        JbMgr mgr = JbMgr.CreateEmpty();
        var ids = SongImport.LogicalIds(mgr);
        SongImport.Added added = SongImport.Add(mgr, ids,
            BBPRecord.FromBbp(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", "degausser-test-scale.bbp"))));
        // A file where the song's folder has to go makes the write fail.
        string jbDirectory = Path.Combine(Save, "jb");
        Directory.CreateDirectory(Path.Combine(jbDirectory, "gak"));
        File.WriteAllText(Path.Combine(jbDirectory, "gak", $"{added.TitleId:x8}"), "in the way");

        Assert.ThrowsAny<IOException>(() => SongImport.WritePayload(jbDirectory, mgr, ids, added));

        Assert.Equal(0xFFFFFFFF, mgr.items[added.Slot].titleID);
        Assert.DoesNotContain(added.TitleId, ids);
    }

    [Fact]
    public void WorkspaceStopsBeforeWritingWhenTheBackupFails()
    {
        string blocked = Path.Combine(root, "not-a-folder");
        File.WriteAllText(blocked, "x");
        string managerPath = Path.Combine(Save, "jb", "mgr.bin");
        ExtDataSafeFile.WriteMirrored(managerPath, JbMgr.CreateEmpty().ToBytes(), SongImport.ManagerContainerSize);
        var workspace = new ExtDataWorkspace(managerPath) { BackupDirectory = blocked };

        Assert.Throws<IOException>(() => workspace.CreateRestorePoint());
    }

    [FixtureFact("DEGAUSSER_MV_EXTDATA")]
    public void DeletingSongsFromARealSaveCanBeRestoredByteForByte()
    {
        string source = FixtureFactAttribute.GetPath("DEGAUSSER_MV_EXTDATA");
        string copy = Path.Combine(root, "real");
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith(Path.Combine("mv", "data")) || relative.StartsWith("voc"))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, relative))!);
            File.Copy(file, Path.Combine(copy, relative));
        }

        Dictionary<string, string> Fingerprint() => Directory.EnumerateFiles(copy, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(copy, f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
        Dictionary<string, string> before = Fingerprint();

        var workspace = new ExtDataWorkspace(Path.Combine(copy, "jb", "mgr.bin")) { BackupDirectory = Backups };
        // This save only has payload folders for its movies, so give three songs one.
        var victims = workspace.Songs.mgr.items.Select((item, slot) => (item, slot))
            .Where(x => x.item.titleID != 0xFFFFFFFF).Take(3).ToList();
        foreach (var victim in victims)
        {
            string folder = Path.Combine(copy, "jb", "gak", $"{victim.item.titleID:x8}");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "pack"), "payload " + victim.slot);
        }
        before = Fingerprint();
        Assert.NotEmpty(victims);
        foreach (var victim in victims)
            workspace.Songs.mgr.items[victim.slot] = JbMgr.Item.Empty;
        workspace.SaveSongs();
        Assert.Equal(0, workspace.RemoveSongPayloads(victims.Select(v => v.item.titleID)));
        Assert.NotEqual(before.Count, Fingerprint().Count);

        ExtDataBackup.Restore(workspace.LastRestorePoint!, copy);

        Assert.Equal(before, Fingerprint());
    }
}
