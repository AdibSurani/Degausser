using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Degausser;

/// <summary>
/// The Library pane: local files and the embedded official catalogue in one list.
/// Every row is one file or one official arrangement; two arrangements of the same
/// song are two rows, told apart by author, parts and where you have them.
/// </summary>
public partial class MainWindow
{
    private sealed class CatalogueCollectionChip
    {
        public CatalogueCollection Collection { get; }
        public string Name => Collection.Name;
        public string Detail => $"{Collection.KindText} · {Collection.SongCount:N0}";
        public string Tooltip { get; }

        public CatalogueCollectionChip(CatalogueCollection collection)
        {
            Collection = collection;
            var lines = new List<string>();
            if (collection.OriginalName.Length > 0) lines.Add(collection.OriginalName);
            if (collection.Parent is not null) lines.Add($"Part of {collection.Parent.Name}");
            if (collection.Children.Count > 0)
                lines.Add($"Includes {string.Join(", ", collection.Children.Take(4).Select(c => c.Name))}{(collection.Children.Count > 4 ? ", …" : string.Empty)}");
            Tooltip = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : collection.Name;
        }
    }

    private BbpCatalogue? catalogue;
    private bool catalogueLoading;
    private bool showWholeCatalogue;
    private CatalogueCollection? activeCatalogueCollection;
    private List<LibrarySong> catalogueCollectionImports = [];
    private DataGridColumn LibraryContextColumn => lstMediaLibrary.Columns[3];

    private async Task EnsureCatalogueLoadedAsync()
    {
        if (catalogue is not null || catalogueLoading || isClosing)
            return;

        catalogueLoading = true;
        try
        {
            BbpCatalogue loaded = await Task.Run(BbpCatalogue.LoadEmbedded);
            if (isClosing) return;
            loaded.RefreshScorefixes(); // the scorefix folder may have changed while it loaded
            catalogue = loaded;
            ToolTip.SetTip(btnScopeCatalogue,
                $"Also list the official arrangements you don't have: BBP Catalogue {loaded.CatalogueVersion}, {loaded.Songs.Count:N0} songs, {loaded.ArrangementCount:N0} arrangements.");
            ApplyLibraryFilter();
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            txtStatusMessage.Text = "Could not load the embedded BBP catalogue.";
        }
        finally
        {
            catalogueLoading = false;
        }
    }

    /// <summary>Re-reads the scorefix folder and updates everything that lists scorefixes.</summary>
    private void ReloadScorefixes()
    {
        ScorefixLibrary library = UserSettingsStore.LoadScorefixes();
        catalogue?.RefreshScorefixes();
        if (activeCatalogueCollection is { Kind: CatalogueCollectionKind.Feature } && library.Count == 0)
            activeCatalogueCollection = null;
        ApplyLibraryFilter();
        txtStatusMessage.Text = library.Count > 0
            ? $"{library.Count} scorefix(es) loaded."
            : "No scorefixes loaded.";
    }

    private Avalonia.Threading.DispatcherTimer? librarySearchTimer;

    /// <summary>Waits for a pause in typing, so a fast typist triggers one update, not one per key.</summary>
    private void LibrarySearchChanged(object? sender, TextChangedEventArgs e)
    {
        librarySearchTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        librarySearchTimer.Stop();
        librarySearchTimer.Tick -= LibrarySearchTimerTick;
        librarySearchTimer.Tick += LibrarySearchTimerTick;
        librarySearchTimer.Start();
    }

    private void LibrarySearchTimerTick(object? sender, EventArgs e)
    {
        librarySearchTimer?.Stop();
        ApplyLibraryFilter();
    }

    private void LibraryScopeClicked(object? sender, RoutedEventArgs e)
    {
        SetLibraryScope(wholeCatalogue: sender == btnScopeCatalogue);
    }

    private void SetLibraryScope(bool wholeCatalogue)
    {
        showWholeCatalogue = wholeCatalogue;
        btnScopeFiles.IsChecked = !wholeCatalogue;
        btnScopeCatalogue.IsChecked = wholeCatalogue;
        ApplyLibraryFilter();
    }

    /// <summary>Rebuilds the Library rows for the search, scope and browsed collection, keeping the selection.</summary>
    private void ApplyLibraryFilter()
    {
        if (lstMediaLibrary is null) return;

        string query = (txtLibrarySearch.Text ?? string.Empty).Trim();
        HashSet<string> selected = (lstMediaLibrary.SelectedItems?.OfType<LibraryRow>() ?? []).Select(x => x.Key).ToHashSet();
        HashSet<uint> saveIds = extDataSessions.SelectMany(x => x.Songs).Select(x => x.mgrItem.titleID).ToHashSet();

        List<LibraryRow> rows = activeCatalogueCollection is CatalogueCollection collection && catalogue is not null
            ? CollectionRows(collection, query, saveIds)
            : SearchRows(query, saveIds);

        // Fill one bound list rather than assigning a new ItemsSource: that rebuilds every row and
        // header (60-100 ms per keystroke, however few rows), while this reuses the grid's rows.
        if (!ReferenceEquals(lstMediaLibrary.ItemsSource, libraryRows))
            lstMediaLibrary.ItemsSource = libraryRows;
        libraryRows.Clear();
        libraryRows.AddRange(rows);
        if (selected.Count > 0)
            foreach (LibraryRow row in rows.Where(x => selected.Contains(x.Key)))
                lstMediaLibrary.SelectedItems!.Add(row);

        UpdateCollectionUi(query);
        UpdateLibraryCount();
        UpdateLibraryEmptyState(rows.Count);
        UpdateImportLabels();
        UpdateActionStates();
    }

    private readonly Avalonia.Collections.AvaloniaList<LibraryRow> libraryRows = new() { ResetBehavior = Avalonia.Collections.ResetBehavior.Reset };

    private List<LibraryRow> SearchRows(string query, HashSet<uint> saveIds)
    {
        // Catalogue search covers English and original titles, artists, works and franchises.
        Dictionary<CatalogueSong, int> rank = [];
        if (catalogue is not null && query.Length > 0)
        {
            int i = 0;
            foreach (CatalogueSong song in catalogue.Search(query))
                rank[song] = i++;
        }

        var searchQuery = LibrarySong.SearchQuery.Create(query);
        var rows = new List<(LibraryRow Row, int Rank)>();
        var localIds = new HashSet<uint>();
        foreach (LibrarySong file in librarySongs)
        {
            uint? id = file.TitleId;
            CatalogueSong? song = id is uint t ? catalogue?.FindSong(t) : null;
            CatalogueArrangement? arrangement = id is uint a ? catalogue?.FindArrangement(a) : null;
            if (id is uint have) localIds.Add(have);

            int songRank = song is not null && rank.TryGetValue(song, out int r) ? r : int.MaxValue;
            if (query.Length > 0 && songRank == int.MaxValue && !file.MatchesSearch(searchQuery))
                continue;
            rows.Add((new LibraryRow(file, song, arrangement, id is uint s && saveIds.Contains(s)), songRank));
        }

        if (showWholeCatalogue && catalogue is not null)
        {
            IEnumerable<CatalogueSong> songs = query.Length > 0 ? rank.Keys : catalogue.Songs;
            foreach (CatalogueSong song in songs)
                foreach (CatalogueArrangement arrangement in song.Arrangements)
                    if (!localIds.Contains(arrangement.TitleId))
                        rows.Add((new LibraryRow(null, song, arrangement, saveIds.Contains(arrangement.TitleId)),
                            rank.GetValueOrDefault(song, int.MaxValue)));
        }

        return rows
            .OrderBy(x => query.Length > 0 ? x.Rank : 0)
            .ThenBy(x => x.Row.SortTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Row.IsLocal ? 0 : 1)
            .ThenBy(x => x.Row.Author, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Row)
            .ToList();
    }

    private List<LibraryRow> CollectionRows(CatalogueCollection collection, string query, HashSet<uint> saveIds)
    {
        // Browsing: the search box narrows the collection's own list, which keeps its order.
        HashSet<CatalogueSong>? matching = query.Length == 0 ? null : catalogue!.Search(query).ToHashSet();
        ILookup<uint, LibrarySong> files = librarySongs.Where(x => x.TitleId.HasValue).ToLookup(x => x.TitleId!.Value);

        var rows = new List<LibraryRow>();
        foreach (CatalogueSongInCollection entry in catalogue!.SongsIn(collection))
        {
            if (matching is not null && !matching.Contains(entry.Song)) continue;
            string context = entry.DescribeIn(collection);
            foreach (CatalogueArrangement arrangement in entry.Song.Arrangements)
            {
                // "Scorefixes" lists the arrangements that have the scorefix, not every version of those songs.
                if (collection == catalogue.Scorefixes && !arrangement.HasScorefix) continue;
                bool inSave = saveIds.Contains(arrangement.TitleId);
                bool any = false;
                foreach (LibrarySong file in files[arrangement.TitleId])
                {
                    rows.Add(new LibraryRow(file, entry.Song, arrangement, inSave, context));
                    any = true;
                }
                if (!any && showWholeCatalogue)
                    rows.Add(new LibraryRow(null, entry.Song, arrangement, inSave, context));
            }
        }
        return rows;
    }

    private void UpdateCollectionUi(string query)
    {
        if (catalogue is null)
        {
            lstCatalogueCollections.IsVisible = pnlCatalogueCollection.IsVisible = false;
            return;
        }

        if (activeCatalogueCollection is CatalogueCollection collection)
        {
            lstCatalogueCollections.IsVisible = false;
            LibraryContextColumn.Header = "In";
            UpdateCatalogueCollectionBar(collection);
            return;
        }

        IReadOnlyList<CatalogueCollection> collections = query.Length == 0
            ? catalogue.FeaturedCollections
            // A one-song collection (usually one artist credit) is no shortcut; keep the chips to one row.
            : catalogue.SearchCollections(query).Where(c => c.SongCount > 1).Take(5).ToList();
        lstCatalogueCollections.ItemsSource = collections.Select(c => new CatalogueCollectionChip(c)).ToList();
        lstCatalogueCollections.IsVisible = collections.Count > 0;
        LibraryContextColumn.Header = "From";
        pnlCatalogueCollection.IsVisible = false;
    }

    private void UpdateCatalogueCollectionBar(CatalogueCollection collection)
    {
        IReadOnlyList<CatalogueSongInCollection> songs = catalogue!.SongsIn(collection);
        HashSet<uint> libraryIds = librarySongs.Where(x => x.TitleId.HasValue).Select(x => x.TitleId!.Value).ToHashSet();
        ExtDataSession? session = ActiveSession;
        HashSet<uint> inSave = session?.Songs.Select(x => x.mgrItem.titleID).ToHashSet() ?? [];

        // One copy per song: the richest arrangement the library has.
        int alreadyInSave = songs.Count(x => x.Song.Arrangements.Any(a => inSave.Contains(a.TitleId)));
        catalogueCollectionImports = songs
            .Where(x => !x.Song.Arrangements.Any(a => inSave.Contains(a.TitleId)))
            .Select(x => x.Song.PreferredArrangement(a => libraryIds.Contains(a.TitleId)))
            .OfType<CatalogueArrangement>()
            .Select(a => librarySongs.First(l => l.TitleId == a.TitleId))
            .ToList();
        int inLibrary = songs.Count(x => x.Song.Arrangements.Any(a => libraryIds.Contains(a.TitleId)));

        txtCatalogueCollectionTitle.Text = collection.Parent is null
            ? collection.Name
            : $"{collection.Name}  ·  {collection.Parent.Name}";
        var detail = new List<string> { collection.KindText, $"{songs.Count:N0} songs", $"{inLibrary:N0} in library" };
        if (session is not null) detail.Add($"{alreadyInSave:N0} already in save");
        if (collection.OriginalName.Length > 0) detail.Add(collection.OriginalName);
        txtCatalogueCollectionDetail.Text = string.Join(" · ", detail);

        btnCatalogueImportCollection.Content = session is null
            ? "Open 3DS content to import"
            : catalogueCollectionImports.Count == 0
                ? "Nothing to import"
                : $"Import {catalogueCollectionImports.Count:N0} to save";
        btnCatalogueImportCollection.IsEnabled = session is not null && catalogueCollectionImports.Count > 0;
        ToolTip.SetTip(btnCatalogueImportCollection,
            "Imports one library copy of every song here that isn't already in the open save, preferring arrangements with English lyrics, then karaoke, then more parts, then native P over DX migrations.");
        pnlCatalogueCollection.IsVisible = true;
    }

    private void CatalogueCollectionClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CatalogueCollectionChip chip) return;
        activeCatalogueCollection = chip.Collection;
        txtLibrarySearch.Text = string.Empty;
        ApplyLibraryFilter();
    }

    private void ClearCatalogueCollectionClicked(object? sender, RoutedEventArgs e)
    {
        activeCatalogueCollection = null;
        ApplyLibraryFilter();
        txtLibrarySearch.Focus();
    }

    private async void ImportCatalogueCollectionClicked(object? sender, RoutedEventArgs e)
    {
        ExtDataSession? session = ActiveSession;
        if (session is null || activeCatalogueCollection is null || catalogueCollectionImports.Count == 0)
            return;

        List<LibrarySong> items = catalogueCollectionImports.ToList();
        string noun = items.Count == 1 ? "song" : "songs";
        DialogChoice confirmation = await MessageDialog.ShowConfirmationAsync(
            this,
            "Import collection",
            $"Import {items.Count:N0} {noun} from {activeCatalogueCollection.Name} into {session.DisplayName}?\n\n"
            + "Songs already in this save are skipped, and each song is imported once, from its richest arrangement. A restore point of the save is made first.",
            "Import");
        if (confirmation != DialogChoice.Yes)
            return;

        await ImportLibraryItemsAsync(session, items);
        ApplyLibraryFilter();
    }

    private List<LibraryRow> SelectedLibraryRows()
        => lstMediaLibrary?.SelectedItems?.OfType<LibraryRow>().ToList() ?? [];

    /// <summary>The selected rows that are local files (official arrangements you don't have can't be imported).</summary>
    private List<LibrarySong> SelectedLibraryFiles()
        => SelectedLibraryRows().Select(x => x.File).OfType<LibrarySong>().ToList();

    private void UpdateLibraryCount()
    {
        int shown = lstMediaLibrary.ItemsSource is System.Collections.ICollection items ? items.Count : 0;
        int selected = lstMediaLibrary.SelectedItems?.Count ?? 0;
        txtLibraryCount.Text = $"{selected:N0} selected · {shown:N0} shown";
    }

    private void UpdateLibraryEmptyState(int shown)
    {
        bool show = shown == 0 && !pnlLibraryScan.IsVisible;
        txtLibraryEmpty.IsVisible = show;
        if (!show) return;

        bool searching = !string.IsNullOrWhiteSpace(txtLibrarySearch.Text);
        if (searching || activeCatalogueCollection is not null)
            txtLibraryEmpty.Text = showWholeCatalogue
                ? "Nothing matches your search."
                : "None of your files match. Choose Whole catalogue to search the official songs you don't have too.";
        else if (librarySongs.Count > 0)
            txtLibraryEmpty.Text = "Nothing to show.";
        else
            txtLibraryEmpty.Text = Directory.Exists(UserSettingsStore.Current.LibraryPath)
                ? $"No songs yet. Put .bbp, .bdx, .bbpm or .bbpv files in\n{UserSettingsStore.Current.LibraryPath}\nor choose another folder in Settings, then press Refresh.\n\nChoose Whole catalogue to browse every official song."
                : $"The library folder\n{UserSettingsStore.Current.LibraryPath}\ndoesn't exist yet. Create it and add songs, or choose another folder in Settings.\n\nChoose Whole catalogue to browse every official song.";
    }

    private void LibraryLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        LibraryRow? row = e.Row.DataContext as LibraryRow;
        e.Row.Classes.Set("notLocal", row is { IsLocal: false });
        ToolTip.SetTip(e.Row, row is null ? null : LibraryRowTooltip(row));
    }

    private static string LibraryRowTooltip(LibraryRow row)
    {
        var lines = new List<string>();
        if (row.File is not null) lines.Add(row.File.FullPath);
        if (row.Arrangement is CatalogueArrangement arrangement)
        {
            if (arrangement.Remark.Length > 0) lines.Add(arrangement.Remark);
            string length = arrangement.LengthText.Length > 0 ? $" · {arrangement.LengthText}" : string.Empty;
            lines.Add($"Official arrangement {arrangement.TitleIdHex} · {arrangement.TypeText}{length}");
            lines.Add(row.File is null
                ? row.InSave ? "In an open save, not in your library" : "Not in your library"
                : row.InSave ? "Also in an open save" : "Not in an open save");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private async Task PlayLibraryRowAsync(LibraryRow? row)
    {
        if (row is null) return;
        if (row.File is not null)
        {
            await PlayLibraryItemAsync(row.File);
            return;
        }
        if (row.Arrangement is not CatalogueArrangement arrangement) return;

        foreach (ExtDataSession session in extDataSessions)
        {
            BBPRecord? record = session.Songs.FirstOrDefault(x => x.mgrItem.titleID == arrangement.TitleId);
            if (record is null) continue;
            PlayItem(record, PlaybackSource.SaveData, session.DisplayName);
            txtStatusMessage.Text = MidiPlayer.Instance.CanPlay
                ? $"Playing {record.Title}"
                : $"Loaded {record.Title}";
            return;
        }
        txtStatusMessage.Text = $"You don't have {row.Title} ({arrangement.Arranger}) in your library or an open save.";
    }

    private async Task FocusCatalogueSearchAsync()
    {
        SetLibraryScope(wholeCatalogue: true);
        await EnsureCatalogueLoadedAsync();
        txtLibrarySearch.Focus();
        txtLibrarySearch.SelectAll();
    }
}
