namespace Degausser;

/// <summary>Portable playable-movie package: JbMgr2 item, movie item,
/// mandatory GAK pack, optional GAK adpcm sidecar, and MOFLEX movie.
/// The GAK adpcm is independent of audio embedded within MOFLEX.</summary>
public sealed class BbpmPackage
{
    public const string JbItemName = "jb/mgr2.item";
    public const string MovieItemName = "mv/mgr.item";
    public const string PackName = "jb/gak/pack";
    public const string AdpcmName = "jb/gak/adpcm";
    public const string MovieName = "mv/data/movie";

    public SimplePackage Package { get; }
    public JbMgr2.Item JbItem { get; }
    public MvMgr.Item MovieItem { get; }
    public bool HasAdpcm => Package.TryGetEntry(AdpcmName, out _);
    public uint SongId => JbItem.item.titleID;
    public uint VideoId => MovieItem.videoID;
    public string Title => (JbItem.item.title ?? string.Empty).Replace("\n", string.Empty);
    public string TitleSimple => (JbItem.item.titleSimple ?? string.Empty).Replace("\n", string.Empty);
    public string Author => JbItem.item.author ?? string.Empty;

    private BbpmPackage(SimplePackage package, JbMgr2.Item jbItem, MvMgr.Item movieItem)
    {
        Package = package;
        JbItem = jbItem;
        MovieItem = movieItem;
    }

    public static BbpmPackage Open(string path)
    {
        SimplePackage package = SimplePackage.Open(path, "BBPM");
        byte[] jbBytes = package.ReadEntryBytes(JbItemName, JbMgr2.Item.ItemBinarySize);
        byte[] mvBytes = package.ReadEntryBytes(MovieItemName, MvMgr.Item.ItemBinarySize);
        package.GetEntry(PackName);
        package.GetEntry(MovieName);

        JbMgr2.Item jb = JbMgr2.Item.Read(jbBytes);
        MvMgr.Item movie = MvMgr.Item.Read(mvBytes);
        if (jb.item.titleID == 0xFFFFFFFF)
            throw new InvalidDataException("BBPM has an empty JbMgr2 item.");
        if (jb.item.videoID == 0 || movie.videoID == 0)
            throw new InvalidDataException("BBPM has no movie ID.");
        if (jb.item.videoID != movie.videoID)
            throw new InvalidDataException($"BBPM cross-link mismatch: Jukebox video ID {jb.item.videoID:X8}, movie ID {movie.videoID:X8}.");
        return new BbpmPackage(package, jb, movie);
    }

    public BBPRecord LoadSongRecord()
    {
        byte[] pack = Package.ReadEntryBytes(PackName);
        BBPRecord record = BBPRecord.FromPack(pack, Package.FilePath);
        record.mgrItem = JbItem.item;
        return record;
    }

    public static void Write(string path, JbMgr2.Item jbItem, MvMgr.Item movieItem, string packPath, string moviePath, string? adpcmPath = null)
    {
        if (jbItem.item.videoID == 0 || movieItem.videoID == 0 || jbItem.item.videoID != movieItem.videoID)
            throw new InvalidDataException("Cannot write BBPM with an invalid Jukebox/Movie cross-link.");
        var entries = new List<SimplePackage.WriteEntry>
        {
            new(JbItemName, jbItem.ToBytes()),
            new(MovieItemName, movieItem.ToBytes()),
            new(PackName, packPath),
        };
        // Optional extdata jb/gak/<song-id>/adpcm is a separate resource
        // from the MOFLEX movie's embedded DSP-ADPCM audio. Do not fabricate
        // it for titles that only have a GAK pack.
        if (adpcmPath is not null)
            entries.Add(new SimplePackage.WriteEntry(AdpcmName, adpcmPath));
        entries.Add(new SimplePackage.WriteEntry(MovieName, moviePath));
        SimplePackage.Write(path, "BBPM", entries);
    }
}
