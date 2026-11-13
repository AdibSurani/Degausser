namespace Degausser;

/// <summary>Portable downloadable-singer package. Mgr2 is canonical; mgr1 is regenerated on install.</summary>
public sealed class BbpvPackage
{
    public const string SingerItemName = "singer/mgr2.item";
    public const string SingerPackName = "singer/pack";
    public const string SingerThumbName = "singer/thumb";

    public SimplePackage Package { get; }
    public SingerMgr2.Item SingerItem { get; }
    public string DatabaseBaseName { get; }
    public string DbBinName => $"voc/db/{DatabaseBaseName}.bin";
    public string DbDdiName => $"voc/db/{DatabaseBaseName}.ddi";
    public string DbDdbName => $"voc/db/{DatabaseBaseName}.ddb";
    public string Artist => SingerItem.item.artist ?? string.Empty;

    private BbpvPackage(SimplePackage package, SingerMgr2.Item singerItem, string databaseBaseName)
    {
        Package = package;
        SingerItem = singerItem;
        DatabaseBaseName = databaseBaseName;
    }

    public static BbpvPackage Open(string path)
    {
        SimplePackage package = SimplePackage.Open(path, "BBPV");
        byte[] singerBytes = package.ReadEntryBytes(SingerItemName, SingerMgr2.Item.ItemBinarySize);
        package.GetEntry(SingerPackName);
        package.GetEntry(SingerThumbName);

        List<string> dbBins = package.Entries
            .Select(e => e.Name)
            .Where(n => n.StartsWith("voc/db/db", StringComparison.Ordinal)
                && n.EndsWith(".bin", StringComparison.Ordinal)
                && n.Count(c => c == '/') == 2)
            .ToList();
        if (dbBins.Count != 1)
            throw new InvalidDataException($"BBPV must contain exactly one voc/db/dbNN.bin entry; found {dbBins.Count}.");
        string dbBase = Path.GetFileNameWithoutExtension(dbBins[0]);
        if (string.IsNullOrWhiteSpace(dbBase))
            throw new InvalidDataException("BBPV voice database name is invalid.");
        package.GetEntry($"voc/db/{dbBase}.ddi");
        package.GetEntry($"voc/db/{dbBase}.ddb");

        SingerMgr2.Item item = SingerMgr2.Item.Read(singerBytes);
        return new BbpvPackage(package, item, dbBase);
    }

    public static void Write(
        string path,
        SingerMgr2.Item singerItem,
        string singerPackPath,
        string thumbPath,
        string dbBinPath,
        string dbDdiPath,
        string dbDdbPath)
    {
        string dbBase = Path.GetFileNameWithoutExtension(dbBinPath);
        if (string.IsNullOrWhiteSpace(dbBase)) throw new InvalidDataException("Voice DB .bin filename is invalid.");
        if (!Path.GetFileNameWithoutExtension(dbDdiPath).Equals(dbBase, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileNameWithoutExtension(dbDdbPath).Equals(dbBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Voice DB .bin/.ddi/.ddb basenames do not match.");

        SimplePackage.Write(path, "BBPV",
        [
            new SimplePackage.WriteEntry(SingerItemName, singerItem.ToBytes()),
            new SimplePackage.WriteEntry(SingerPackName, singerPackPath),
            new SimplePackage.WriteEntry(SingerThumbName, thumbPath),
            new SimplePackage.WriteEntry($"voc/db/{dbBase}.bin", dbBinPath),
            new SimplePackage.WriteEntry($"voc/db/{dbBase}.ddi", dbDdiPath),
            new SimplePackage.WriteEntry($"voc/db/{dbBase}.ddb", dbDdbPath),
        ]);
    }
}
