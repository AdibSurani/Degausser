using System.IO;

namespace Degausser;

/// <summary>
/// Represents one Band Brothers P extdata set as a logical workspace. The
/// entry point is jb/mgr.bin; movies and downloadable singers live in sibling
/// directories under the same extdata root.
/// </summary>
public sealed class ExtDataWorkspace
{
    private const int JbMgr2ContainerSize = 480256;
    private const int MvMgrContainerSize = 392192;
    private const int SingerMgrContainerSize = 481280;
    private const int SingerMgr2ContainerSize = 481280;

    public sealed record MovieEntry(
        int MovieSlot,
        int JbMgr2Slot,
        JbMgr2.Item JbItem,
        MvMgr.Item MovieItem,
        string PackPath,
        string MoviePath)
    {
        public uint SongId => JbItem.item.titleID;
        public uint VideoId => MovieItem.videoID;
        public string SongTitle => (JbItem.item.title ?? string.Empty).Replace("\n", string.Empty);
        public string Author => JbItem.item.author ?? string.Empty;
        public string MovieTitle => (MovieItem.title ?? string.Empty).Replace("\n", " · ");
        public bool HasVocals => JbItem.item.flags.HasVocals;
        public bool FilesPresent => File.Exists(PackPath) && File.Exists(MoviePath);
    }

    public sealed record SingerEntry(
        int Slot,
        SingerMgr2.Item Item,
        string SingerPackPath,
        string ThumbPath,
        string? DatabaseBaseName,
        string? DbBinPath,
        string? DbDdiPath,
        string? DbDdbPath)
    {
        public string Artist => Item.item.artist ?? string.Empty;
        public string Greeting => Item.item.greeting ?? string.Empty;

        /// <summary>The greeting on one line, for list display.</summary>
        public string GreetingLine => string.Join(" ", Greeting.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        public ushort VoiceCode => unchecked((ushort)(Item.item.unk5 & 0xFFFF));
        public string VoiceCodeHex => VoiceCode.ToString("X4");
        public string Database => DatabaseBaseName ?? "?";
        public bool FilesPresent => File.Exists(SingerPackPath)
            && File.Exists(ThumbPath)
            && DbBinPath is not null && File.Exists(DbBinPath)
            && DbDdiPath is not null && File.Exists(DbDdiPath)
            && DbDdbPath is not null && File.Exists(DbDdbPath);
    }

    public JbManager Songs { get; }

    public string EntryFilePath => Songs.FilePath;
    public string JbDirectory => Songs.Directory;
    // Compatibility: existing song code expects this to mean jb/.
    public string RootDirectory => JbDirectory;
    public string ExtDataRootDirectory => Path.GetDirectoryName(JbDirectory) ?? JbDirectory;

    public string DisplayName
    {
        get
        {
            var directory = new DirectoryInfo(ExtDataRootDirectory);
            return string.IsNullOrWhiteSpace(directory.Name) ? "3DS Content" : directory.Name;
        }
    }

    /// <summary>
    /// Finds the song manager (<c>jb/mgr.bin</c>) for a folder the user picked as their save data. The folder is
    /// normally the extdata root; the <c>jb</c> folder itself and a parent holding a <c>P</c> folder (an SD card's
    /// root, where the English patch keeps the save) are accepted too. Null if none of them has one.
    /// </summary>
    public static string? FindManagerFile(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        string inJb = Path.Combine(folder, "jb", "mgr.bin");
        if (File.Exists(inJb)) return inJb;
        // mv/ and singer/ have a mgr.bin of their own, so only the jb folder itself counts.
        string direct = Path.Combine(folder, "mgr.bin");
        if (string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), "jb", StringComparison.OrdinalIgnoreCase) && File.Exists(direct))
            return direct;
        string patched = Path.Combine(folder, "P", "jb", "mgr.bin");
        if (File.Exists(patched)) return patched;
        return null;
    }

    public ExtDataWorkspace(string mgrPath)
    {
        Songs = new JbManager(mgrPath);
    }

    public List<BBPRecord> ReadSongs(Action<string>? warning = null)
    {
        var records = new List<BBPRecord>();
        for (int i = 0; i < Songs.mgr.items.Length; i++)
        {
            var item = Songs.mgr.items[i];
            if (item.titleID == 0xFFFFFFFF || !item.flags.OnSD)
                continue;

            string packPath = Path.Combine(JbDirectory, "gak", $"{item.titleID:x8}", "pack");
            try
            {
                records.Add(BBPRecord.FromJbMgr(item, packPath, i));
            }
            catch (Exception exception)
            {
                warning?.Invoke($"Could not load save slot {i} ({item.title}): {exception.Message}");
            }
        }
        return records;
    }

    public List<MovieEntry> ReadMovies(Action<string>? warning = null)
    {
        string jb2Path = Path.Combine(JbDirectory, "mgr2.bin");
        string mvPath = Path.Combine(ExtDataRootDirectory, "mv", "mgr.bin");
        if (!File.Exists(jb2Path) || !File.Exists(mvPath))
            return [];

        JbMgr2 jb2 = JbMgr2.Read(ExtDataSafeFile.ReadDecompressed(jb2Path));
        MvMgr mv = MvMgr.Read(ExtDataSafeFile.ReadDecompressed(mvPath));
        var byVideo = new Dictionary<uint, (int Slot, JbMgr2.Item Item)>();
        for (int i = 0; i < jb2.items.Length; i++)
        {
            JbMgr2.Item item = jb2.items[i];
            if (item.item.titleID == 0xFFFFFFFF || item.item.videoID == 0)
                continue;
            byVideo.TryAdd(item.item.videoID, (i, item));
        }

        var result = new List<MovieEntry>();
        for (int i = 0; i < mv.items.Length; i++)
        {
            MvMgr.Item movie = mv.items[i];
            if (movie.hasItem == 0 || movie.videoID == 0)
                continue;
            if (!byVideo.TryGetValue(movie.videoID, out var song))
            {
                warning?.Invoke($"Movie slot {i} ({movie.title}) has video ID {movie.videoID:X8} but no linked JbMgr2 song.");
                continue;
            }
            string pack = Path.Combine(JbDirectory, "gak", $"{song.Item.item.titleID:x8}", "pack");
            string video = Path.Combine(ExtDataRootDirectory, "mv", "data", $"{movie.videoID:x8}");
            result.Add(new MovieEntry(i, song.Slot, song.Item, movie, pack, video));
        }
        return result;
    }

    public List<SingerEntry> ReadSingers(Action<string>? warning = null)
    {
        string mgr2Path = Path.Combine(ExtDataRootDirectory, "singer", "mgr2.bin");
        if (!File.Exists(mgr2Path))
            return [];

        SingerMgr2 mgr2 = SingerMgr2.Read(ExtDataSafeFile.ReadDecompressed(mgr2Path));
        string dbDirectory = Path.Combine(ExtDataRootDirectory, "voc", "db");
        Dictionary<byte, List<string>> databasesByVoiceByte = IndexVoiceDatabases(dbDirectory, warning);
        var result = new List<SingerEntry>();

        // Observed downloadable singers start at slot 11. Slots 0..10 are the
        // local/profile singer area and are intentionally not exported as BBPV.
        for (int i = 11; i < mgr2.items.Length; i++)
        {
            SingerMgr2.Item item = mgr2.items[i];
            if (item.item.unk4 < 0 || string.IsNullOrWhiteSpace(item.item.artist))
                continue;

            string singerDirectory = Path.Combine(ExtDataRootDirectory, "singer", "data", i.ToString("0000"));
            byte voiceByte = unchecked((byte)(item.item.unk5 & 0xFF));
            string? db = databasesByVoiceByte.TryGetValue(voiceByte, out List<string>? matches) && matches.Count == 1
                ? matches[0]
                : null;
            if (db is null && matches is { Count: > 1 })
                warning?.Invoke($"Singer slot {i} ({item.item.artist}) matches multiple voice DBs for code byte {voiceByte:X2}.");

            result.Add(new SingerEntry(
                i,
                item,
                Path.Combine(singerDirectory, "pack"),
                Path.Combine(singerDirectory, "thumb"),
                db,
                db is null ? null : Path.Combine(dbDirectory, db + ".bin"),
                db is null ? null : Path.Combine(dbDirectory, db + ".ddi"),
                db is null ? null : Path.Combine(dbDirectory, db + ".ddb")));
        }
        return result;
    }

    public BBPRecord LoadMovieSong(MovieEntry movie)
        => BBPRecord.FromJbMgr(movie.JbItem.item, movie.PackPath, movie.JbMgr2Slot);

    public HashSet<uint> GetLogicalSongIds()
    {
        var ids = new HashSet<uint>(Songs.mgr.items
            .Where(item => item.titleID != 0xFFFFFFFF)
            .Select(item => item.titleID));

        string jb2Path = Path.Combine(JbDirectory, "mgr2.bin");
        if (File.Exists(jb2Path))
        {
            JbMgr2 jb2 = JbMgr2.Read(ExtDataSafeFile.ReadDecompressed(jb2Path));
            ids.UnionWith(jb2.items
                .Where(item => item.item.titleID != 0xFFFFFFFF)
                .Select(item => item.item.titleID));
        }
        return ids;
    }

    public void ImportMoviePackage(string packagePath, ContentTranslation.Manifest? englishTranslations = null)
    {
        BbpmPackage package = BbpmPackage.Open(packagePath);
        JbMgr2.Item songToImport = package.JbItem;
        MvMgr.Item movieToImport = package.MovieItem;
        byte[]? localizedPack = null;
        if (englishTranslations is not null
            && ContentTranslation.TryTranslateMovie(package, englishTranslations,
                out songToImport, out movieToImport))
        {
            try
            {
                localizedPack = ContentTranslation.LocalizeMoviePack(
                    package.Package.ReadEntryBytes(BbpmPackage.PackName), ref songToImport);
            }
            catch (InvalidDataException)
            {
                // A pack in a layout we can't rewrite keeps its Japanese title; the
                // translated manager items are still imported.
            }
        }
        string jb2Path = Path.Combine(JbDirectory, "mgr2.bin");
        string mvPath = Path.Combine(ExtDataRootDirectory, "mv", "mgr.bin");
        JbMgr2 jb2 = File.Exists(jb2Path)
            ? JbMgr2.Read(ExtDataSafeFile.ReadDecompressed(jb2Path))
            : JbMgr2.CreateEmpty();
        MvMgr mv = File.Exists(mvPath)
            ? MvMgr.Read(ExtDataSafeFile.ReadDecompressed(mvPath))
            : MvMgr.CreateEmpty();

        if (Songs.mgr.items.Any(i => i.titleID == package.SongId))
            throw new InvalidOperationException($"Song ID {package.SongId:X8} already exists in jb/mgr.bin.");
        if (jb2.items.Any(i => i.item.titleID == package.SongId))
            throw new InvalidOperationException($"Song ID {package.SongId:X8} already exists in jb/mgr2.bin.");
        if (jb2.items.Any(i => i.item.videoID == package.VideoId && i.item.titleID != 0xFFFFFFFF))
            throw new InvalidOperationException($"Video ID {package.VideoId:X8} is already linked from jb/mgr2.bin.");
        if (mv.items.Any(i => i.hasItem != 0 && i.videoID == package.VideoId))
            throw new InvalidOperationException($"Video ID {package.VideoId:X8} already exists in mv/mgr.bin.");

        var logicalSongIds = new HashSet<uint>(Songs.mgr.items
            .Where(i => i.titleID != 0xFFFFFFFF)
            .Select(i => i.titleID));
        logicalSongIds.UnionWith(jb2.items
            .Where(i => i.item.titleID != 0xFFFFFFFF)
            .Select(i => i.item.titleID));
        if (logicalSongIds.Count >= JbMgr.ItemCount)
            throw new InvalidOperationException($"The logical jukebox is full ({JbMgr.ItemCount:N0} entries).");

        int jbSlot = FindFreeJbMgr2Slot(jb2);
        int mvSlot = FindFreeMovieSlot(mv);

        string songDirectory = Path.Combine(JbDirectory, "gak", $"{package.SongId:x8}");
        string movieDirectory = Path.Combine(ExtDataRootDirectory, "mv", "data");
        CreateRestorePoint();
        Directory.CreateDirectory(songDirectory);
        Directory.CreateDirectory(movieDirectory);
        string packDestination = Path.Combine(songDirectory, "pack");
        string adpcmDestination = Path.Combine(songDirectory, "adpcm");
        string movieDestination = Path.Combine(movieDirectory, $"{package.VideoId:x8}");
        // Refuse stale sidecar files even for a BBPM without adpcm: otherwise
        // a pack-only import would inadvertently pick up unrelated audio.
        if (File.Exists(packDestination) || File.Exists(adpcmDestination) || File.Exists(movieDestination))
            throw new IOException("A destination payload path already exists even though its manager ID is free; refusing to overwrite it.");

        byte[] previousMv = mv.ToBytes();
        bool mvPublished = false;
        try
        {
            if (localizedPack is not null) File.WriteAllBytes(packDestination, localizedPack);
            else package.Package.CopyEntryTo(BbpmPackage.PackName, packDestination);
            if (package.HasAdpcm)
                package.Package.CopyEntryTo(BbpmPackage.AdpcmName, adpcmDestination);
            package.Package.CopyEntryTo(BbpmPackage.MovieName, movieDestination);
            jb2.items[jbSlot] = songToImport;
            mv.items[mvSlot] = movieToImport;
            // Publish the movie catalogue first, then the jukebox foreign-key. If a
            // host-side write fails between the two, an orphan movie is preferable
            // to a visible Jukebox item whose video target does not yet exist.
            ExtDataSafeFile.WriteMirrored(mvPath, mv.ToBytes(), MvMgrContainerSize);
            mvPublished = true;
            ExtDataSafeFile.WriteMirrored(jb2Path, jb2.ToBytes(), JbMgr2ContainerSize);
        }
        catch
        {
            if (mvPublished)
            {
                // The movie list already names this video: take it back before deleting the
                // payload, and if that fails too keep the files so the entry isn't dangling.
                bool restored = true;
                try
                {
                    ExtDataSafeFile.WriteMirrored(mvPath, previousMv, MvMgrContainerSize);
                }
                catch
                {
                    restored = false;
                }
                if (!restored) throw;
            }
            TryDelete(packDestination);
            TryDelete(adpcmDestination);
            TryDelete(movieDestination);
            throw;
        }
    }

    /// <summary>
    /// Translates movies already in the save, the way importing them with English on
    /// would: manager titles, descriptions and authors, plus the title and karaoke inside
    /// each pack. Movies whose Japanese originals no longer match are left alone, so this
    /// can be run again. Returns the video IDs that changed.
    /// </summary>
    public List<uint> TranslateInstalledMovies(ContentTranslation.Manifest manifest, bool write = true)
    {
        string jb2Path = Path.Combine(JbDirectory, "mgr2.bin");
        string mvPath = Path.Combine(ExtDataRootDirectory, "mv", "mgr.bin");
        var changed = new List<uint>();
        if (!File.Exists(jb2Path) || !File.Exists(mvPath)) return changed;

        JbMgr2 jb2 = JbMgr2.Read(ExtDataSafeFile.ReadDecompressed(jb2Path));
        MvMgr mv = MvMgr.Read(ExtDataSafeFile.ReadDecompressed(mvPath));
        var packs = new Dictionary<string, byte[]>();
        foreach (MovieEntry entry in ReadMovies())
        {
            if (!ContentTranslation.HasMovie(manifest, entry.VideoId, entry.SongId) || !File.Exists(entry.PackPath))
                continue;
            JbMgr2.Item original = jb2.items[entry.JbMgr2Slot];
            MvMgr.Item originalMovie = mv.items[entry.MovieSlot];
            JbMgr2.Item song = original;
            MvMgr.Item movie = originalMovie;
            // The managers and the pack are judged separately, so a run that was cut off
            // between writing them can be finished by running again.
            byte[] pack = File.ReadAllBytes(entry.PackPath);
            bool packJapanese = HasJapanese(BBPRecord.FromPack(pack).gak);
            if (packJapanese || HasJapanese(song.item.title) || HasJapanese(movie.title))
                ContentTranslation.TryTranslateMovie(manifest, entry.VideoId, entry.SongId, ref song, ref movie);
            byte[] localized = pack;
            if (packJapanese)
            {
                try
                {
                    localized = ContentTranslation.LocalizeMoviePack(pack, ref song);
                }
                catch (InvalidDataException)
                {
                }
            }
            bool packChanged = !localized.AsSpan().SequenceEqual(pack);
            if (!packChanged && song.ToBytes().AsSpan().SequenceEqual(original.ToBytes())
                && movie.ToBytes().AsSpan().SequenceEqual(originalMovie.ToBytes()))
                continue;
            if (packChanged) packs[entry.PackPath] = localized;
            jb2.items[entry.JbMgr2Slot] = song;
            mv.items[entry.MovieSlot] = movie;
            changed.Add(entry.VideoId);
        }
        if (!write || changed.Count == 0) return changed;

        CreateRestorePoint();
        foreach (var (path, bytes) in packs)
        {
            PreserveFile(path);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        ExtDataSafeFile.WriteMirrored(mvPath, mv.ToBytes(), MvMgrContainerSize);
        ExtDataSafeFile.WriteMirrored(jb2Path, jb2.ToBytes(), JbMgr2ContainerSize);
        return changed;
    }

    /// <summary>Kana or kanji in the title or lyrics; the dot and long-vowel mark are
    /// part of romanised lyrics too, so they don't count.</summary>
    private static bool HasJapanese(Gak gak) => HasJapanese(gak.title + gak.karaokeLyrics);

    private static bool HasJapanese(string? text)
        => (text ?? string.Empty).Any(c =>
            c is >= '\u3040' and <= '\u30FF' and not ('\u30FB' or '\u30FC') or >= '\u4E00' and <= '\u9FFF' or >= '\uFF66' and <= '\uFF9F');

    public void ExportMoviePackage(MovieEntry movie, string packagePath)
    {
        if (!File.Exists(movie.PackPath)) throw new FileNotFoundException("Movie song pack is missing.", movie.PackPath);
        if (!File.Exists(movie.MoviePath)) throw new FileNotFoundException("MOFLEX movie payload is missing.", movie.MoviePath);
        // Extdata stores the optional sidecar alongside pack in jb/gak/<id>/.
        // Neither MOFLEX nor an M4A is a substitute for this file.
        string adpcmPath = Path.Combine(Path.GetDirectoryName(movie.PackPath)!, "adpcm");
        BbpmPackage.Write(packagePath, movie.JbItem, movie.MovieItem, movie.PackPath, movie.MoviePath,
            File.Exists(adpcmPath) ? adpcmPath : null);
    }

    public void ImportSingerPackage(string packagePath, ContentTranslation.Manifest? englishTranslations = null)
    {
        BbpvPackage package = BbpvPackage.Open(packagePath);
        SingerMgr2.Item itemToImport = package.SingerItem;
        if (englishTranslations is not null)
            ContentTranslation.TryTranslateSinger(package, englishTranslations, out itemToImport);
        string singerDirectory = Path.Combine(ExtDataRootDirectory, "singer");
        string mgr2Path = Path.Combine(singerDirectory, "mgr2.bin");
        string mgrPath = Path.Combine(singerDirectory, "mgr.bin");
        SingerMgr2 mgr2 = File.Exists(mgr2Path)
            ? SingerMgr2.Read(ExtDataSafeFile.ReadDecompressed(mgr2Path))
            : SingerMgr2.CreateEmpty();
        SingerMgr mgr = File.Exists(mgrPath)
            ? SingerMgr.Read(ExtDataSafeFile.ReadDecompressed(mgrPath))
            : SingerMgr.CreateEmpty();

        int slot = -1;
        for (int i = 11; i < SingerMgr2.ItemCount; i++)
        {
            if (mgr2.items[i].item.unk4 < 0 && string.IsNullOrWhiteSpace(mgr2.items[i].item.artist))
            {
                slot = i;
                break;
            }
        }
        if (slot < 0)
            throw new InvalidOperationException("No free downloadable-singer slot (11..1010) is available.");

        string dbDirectory = Path.Combine(ExtDataRootDirectory, "voc", "db");
        CreateRestorePoint();
        Directory.CreateDirectory(dbDirectory);
        string dbBin = Path.Combine(dbDirectory, package.DatabaseBaseName + ".bin");
        string dbDdi = Path.Combine(dbDirectory, package.DatabaseBaseName + ".ddi");
        string dbDdb = Path.Combine(dbDirectory, package.DatabaseBaseName + ".ddb");
        EnsurePackageEntryCanInstall(package.Package, package.DbBinName, dbBin);
        EnsurePackageEntryCanInstall(package.Package, package.DbDdiName, dbDdi);
        EnsurePackageEntryCanInstall(package.Package, package.DbDdbName, dbDdb);

        string slotDirectory = Path.Combine(singerDirectory, "data", slot.ToString("0000"));
        if (Directory.Exists(slotDirectory) && Directory.EnumerateFileSystemEntries(slotDirectory).Any())
            throw new IOException($"Singer slot directory already contains data: {slotDirectory}");
        Directory.CreateDirectory(slotDirectory);

        byte[] previousMgr = mgr.ToBytes();
        bool mgrWritten = false;
        SingerMgr2.Item item = itemToImport;
        item.item.unk4 = checked((short)slot);
        mgr2.items[slot] = item;
        mgr.items[slot] = item.ToLegacyItem();

        var created = new List<string>();
        try
        {
            CopyIfMissing(package.Package, BbpvPackage.SingerPackName, Path.Combine(slotDirectory, "pack"), created);
            CopyIfMissing(package.Package, BbpvPackage.SingerThumbName, Path.Combine(slotDirectory, "thumb"), created);
            CopyIfMissing(package.Package, package.DbBinName, dbBin, created);
            CopyIfMissing(package.Package, package.DbDdiName, dbDdi, created);
            CopyIfMissing(package.Package, package.DbDdbName, dbDdb, created);
            // The legacy manager is a derived compatibility view; publish it
            // first and make mgr2 (the canonical extended record) visible last.
            ExtDataSafeFile.WriteMirrored(mgrPath, mgr.ToBytes(), SingerMgrContainerSize);
            mgrWritten = true;
            ExtDataSafeFile.WriteMirrored(mgr2Path, mgr2.ToBytes(), SingerMgr2ContainerSize);
        }
        catch
        {
            // Don't leave the legacy manager listing a singer that mgr2 never got.
            if (mgrWritten)
            {
                try { ExtDataSafeFile.WriteMirrored(mgrPath, previousMgr, SingerMgrContainerSize); } catch { }
            }
            foreach (string path in created) TryDelete(path);
            throw;
        }
    }

    public void ExportSingerPackage(SingerEntry singer, string packagePath)
    {
        if (!File.Exists(singer.SingerPackPath)) throw new FileNotFoundException("Singer pack is missing.", singer.SingerPackPath);
        if (!File.Exists(singer.ThumbPath)) throw new FileNotFoundException("Singer thumbnail is missing.", singer.ThumbPath);
        if (singer.DbBinPath is null || singer.DbDdiPath is null || singer.DbDdbPath is null
            || !File.Exists(singer.DbBinPath) || !File.Exists(singer.DbDdiPath) || !File.Exists(singer.DbDdbPath))
            throw new FileNotFoundException($"Could not resolve the voice database for singer slot {singer.Slot} ({singer.Artist}).");
        BbpvPackage.Write(packagePath, singer.Item, singer.SingerPackPath, singer.ThumbPath,
            singer.DbBinPath, singer.DbDdiPath, singer.DbDdbPath);
    }

    public void SaveSongs()
    {
        CreateRestorePoint();
        Songs.Save();
    }

    /// <summary>
    /// Where restore points are kept (outside the save, so nothing extra lands on the SD card).
    /// Null turns them off, which is what library code and tests get by default.
    /// </summary>
    public string? BackupDirectory { get; set; }

    /// <summary>The restore point made by the latest write, if any.</summary>
    public string? LastRestorePoint { get; private set; }

    /// <summary>
    /// Snapshots the managers before a write. Failing here stops the write: an operation
    /// that can't be undone should not run just because the backup folder is unwritable.
    /// </summary>
    public string? CreateRestorePoint()
    {
        if (BackupDirectory is null) return LastRestorePoint = null;
        try
        {
            return LastRestorePoint = ExtDataBackup.Create(ExtDataRootDirectory, BackupDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not back up the save first, so nothing was changed: {exception.Message}", exception);
        }
    }

    /// <summary>Keeps the current copy of a payload file in the latest restore point before it is rewritten.</summary>
    public void PreserveFile(string path)
    {
        if (LastRestorePoint is not null)
            ExtDataBackup.Preserve(LastRestorePoint, ExtDataRootDirectory, path);
    }

    /// <summary>
    /// Removes the GAK folders of songs that were just taken out of the manager. Each folder
    /// is copied into the latest restore point first; if that fails the folder is kept.
    /// Returns how many could not be removed.
    /// </summary>
    public int RemoveSongPayloads(IEnumerable<uint> titleIds, Action<string>? warning = null)
    {
        int failed = 0;
        foreach (uint id in titleIds)
        {
            string folder = Path.Combine(JbDirectory, "gak", $"{id:x8}");
            if (!Directory.Exists(folder)) continue;
            try
            {
                PreserveFile(folder);
                Directory.Delete(folder, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
                warning?.Invoke($"Could not remove {folder}: {exception.Message}");
            }
        }
        return failed;
    }

    private static int FindFreeJbMgr2Slot(JbMgr2 mgr)
    {
        for (int i = 0; i < JbMgr2.ItemCount; i++)
            if (mgr.items[i].item.titleID == 0xFFFFFFFF) return i;
        throw new InvalidOperationException($"jb/mgr2.bin is full ({JbMgr2.ItemCount:N0} V2 records).");
    }

    private static int FindFreeMovieSlot(MvMgr mgr)
    {
        for (int i = 0; i < MvMgr.ItemCount; i++)
            if (mgr.items[i].hasItem == 0 || mgr.items[i].videoID == 0) return i;
        throw new InvalidOperationException($"mv/mgr.bin is full ({MvMgr.ItemCount:N0} movie records).");
    }

    private static Dictionary<byte, List<string>> IndexVoiceDatabases(string directory, Action<string>? warning)
    {
        var result = new Dictionary<byte, List<string>>();
        if (!Directory.Exists(directory)) return result;
        Span<byte> prefix = stackalloc byte[4];
        foreach (string path in Directory.EnumerateFiles(directory, "db*.bin", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Read(prefix) != prefix.Length || prefix[0] != (byte)'D' || prefix[1] != (byte)'B')
                    continue;
                byte code = prefix[3];
                string basename = Path.GetFileNameWithoutExtension(path);
                if (!result.TryGetValue(code, out List<string>? list)) result[code] = list = [];
                list.Add(basename);
            }
            catch (Exception exception)
            {
                warning?.Invoke($"Could not inspect voice DB {path}: {exception.Message}");
            }
        }
        return result;
    }

    private static void EnsurePackageEntryCanInstall(SimplePackage package, string entryName, string destination)
    {
        if (!File.Exists(destination)) return;
        SimplePackage.Entry entry = package.GetEntry(entryName);
        var info = new FileInfo(destination);
        if (info.Length != entry.Length || !StreamsEqual(package.OpenEntry(entryName), new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
            throw new IOException($"Voice database destination already exists with different content: {destination}");
    }

    private static void CopyIfMissing(SimplePackage package, string entryName, string destination, List<string> created)
    {
        if (File.Exists(destination)) return;
        package.CopyEntryTo(entryName, destination);
        created.Add(destination);
    }

    private static bool StreamsEqual(Stream a, Stream b)
    {
        using (a) using (b)
        {
            byte[] aa = new byte[128 * 1024];
            byte[] bb = new byte[128 * 1024];
            while (true)
            {
                int ar = a.Read(aa, 0, aa.Length);
                int br = b.Read(bb, 0, bb.Length);
                if (ar != br) return false;
                if (ar == 0) return true;
                if (!aa.AsSpan(0, ar).SequenceEqual(bb.AsSpan(0, br))) return false;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
