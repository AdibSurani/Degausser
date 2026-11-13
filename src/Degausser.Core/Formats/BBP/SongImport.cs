namespace Degausser;

public sealed class SongImportException(string message) : InvalidOperationException(message) { }

/// <summary>
/// Adding a song to an extdata song catalogue, independent of how files are
/// stored: callers write <see cref="Added.Pack"/> to <see cref="Added.PackPath"/>
/// and then save the updated <see cref="JbMgr"/> (see ExtDataSafeFile.Encode).
/// </summary>
public static class SongImport
{
    /// <summary>The fixed SafeFile container size of jb/mgr.bin.</summary>
    public const int ManagerContainerSize = 1_155_072;

    public sealed record Added(int Slot, uint TitleId, byte[] Pack, bool EnglishLyrics = false)
    {
        /// <summary>Path relative to the extdata root, e.g. "jb/gak/80001234/pack".</summary>
        public string PackPath => $"jb/gak/{TitleId:x8}/pack";
    }

    /// <summary>Song IDs already used by the save's primary and secondary catalogues.</summary>
    public static HashSet<uint> LogicalIds(JbMgr mgr, JbMgr2? mgr2 = null)
    {
        var ids = new HashSet<uint>(mgr.items.Where(i => i.titleID != 0xFFFFFFFF).Select(i => i.titleID));
        if (mgr2 is not null)
            ids.UnionWith(mgr2.items.Where(i => i.item.titleID != 0xFFFFFFFF).Select(i => i.item.titleID));
        return ids;
    }

    /// <summary>
    /// A copy of <paramref name="record"/> with its bundled romaji title and lyrics, or null if
    /// Degausser has no scorefix for this song. <paramref name="record"/> itself is unchanged.
    /// Used for both import and preview, so what you hear is what you import.
    /// </summary>
    /// <exception cref="InvalidDataException">The scorefix doesn't fit this copy of the song.</exception>
    public static BBPRecord? WithEnglishLyrics(BBPRecord record)
    {
        if (ScorefixLibrary.Current.Find(record) is not string scorefix) return null;
        // Clone through the pack only: ToBbpBytes() would zero the scores, singer and icon.
        JbMgr.Item original = record.mgrItem;
        BBPRecord copy = BBPRecord.FromPack(record.Recompile(), record.FullPath);
        copy.mgrItem = original;
        Scorefix.Apply(copy, scorefix);
        return copy;
    }

    /// <summary>
    /// Puts <paramref name="record"/> in the first free slot of <paramref name="mgr"/> and
    /// returns its pack. Custom songs (0x8000xxxx IDs) get the next unused custom ID;
    /// any other ID already in the save is refused rather than duplicated.
    /// With <paramref name="englishLyrics"/>, a song that has a bundled scorefix is
    /// imported with its romaji title and lyrics; <paramref name="record"/> itself
    /// is left unchanged.
    /// </summary>
    public static Added Add(JbMgr mgr, HashSet<uint> existingIds, BBPRecord record, bool englishLyrics = false)
    {
        if (existingIds.Count >= JbMgr.ItemCount)
            throw new SongImportException($"The save's song catalogue is full ({JbMgr.ItemCount:N0} songs).");
        int slot = Array.FindIndex(mgr.items, i => i.titleID == 0xFFFFFFFF);
        if (slot < 0)
            throw new SongImportException($"The save's song catalogue is full ({JbMgr.ItemCount:N0} songs).");

        bool scorefixed = false;
        if (englishLyrics)
        {
            try
            {
                if (WithEnglishLyrics(record) is BBPRecord english)
                {
                    record = english;
                    scorefixed = true;
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or FormatException)
            {
                throw new SongImportException($"Its English lyrics don't fit this copy of the song: {exception.Message}");
            }
        }

        JbMgr.Item item = record.mgrItem;
        if (item.titleID >> 16 == 0x8000)
        {
            item.titleID = Enumerable.Range(0, 0x10000).Select(i => (uint)(0x80000000 + i))
                .FirstOrDefault(id => !existingIds.Contains(id), 0xFFFFFFFF);
            if (item.titleID == 0xFFFFFFFF)
                throw new SongImportException("No unused custom song ID is left in this save.");
        }
        else if (existingIds.Contains(item.titleID))
        {
            throw new SongImportException($"Song 0x{item.titleID:X8} is already in this save.");
        }

        int originalGakTitleId = record.gak.titleID;
        byte[] pack;
        try
        {
            record.gak.titleID = (int)item.titleID;
            pack = record.Recompile();
        }
        finally
        {
            record.gak.titleID = originalGakTitleId;
        }

        mgr.items[slot] = item;
        existingIds.Add(item.titleID);
        return new Added(slot, item.titleID, pack, scorefixed);
    }

    /// <summary>
    /// Writes an added song's pack into the save. <see cref="Add"/> has already put the song in
    /// the manager, so if the pack cannot be written the manager entry is taken out again;
    /// otherwise a later manager save would list a song whose file does not exist.
    /// </summary>
    public static void WritePayload(string jbDirectory, JbMgr mgr, HashSet<uint> existingIds, Added added)
    {
        string folder = Path.Combine(jbDirectory, "gak", $"{added.TitleId:x8}");
        bool created = !Directory.Exists(folder);
        try
        {
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, "pack");
            string temporary = destination + ".tmp";
            File.WriteAllBytes(temporary, added.Pack);
            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            mgr.items[added.Slot] = JbMgr.Item.Empty;
            existingIds.Remove(added.TitleId);
            try { if (created && Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            throw;
        }
    }
}
