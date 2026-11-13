using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Melanchall.DryWetMidi.Core;

namespace Degausser;

/// <summary>
/// Cross-platform desktop shell for Degausser. Format interpretation and
/// conversion remain in Degausser.Core; this project owns desktop UI, file
/// pickers and preview playback only.
/// </summary>
public partial class MainWindow : Window
{
    private enum PlaybackSource
    {
        Library,
        SaveData,
        MovieSaveData,
        OpenedFile
    }

    private sealed record KaraokeLineSpan(int Start, int Length, KaraokeLineView View);

    private Karaoke karaoke = new();
    private readonly ObservableCollection<ExtDataSession> extDataSessions = [];
    private readonly List<KaraokeLineSpan> karaokeLines = [];
    private List<LibrarySong> librarySongs = [];
    private BBPRecord? currentRecord;
    private PlaybackSource currentPlaybackSource = PlaybackSource.OpenedFile;
    private ExtDataSession? currentSaveSession;
    private bool changingExtDataSession;
    private bool restoringStartupView = true;
    private int currentKaraokeLine = -1;
    private CancellationTokenSource? libraryScanCancellation;
    private string? loadedLibraryPath;
    private int libraryScanGeneration;
    private int libraryPreviewGeneration;
    private bool isClosing;

    private ExtDataSession? ActiveSession => lstExtDataSessions?.SelectedItem as ExtDataSession;
    private JbManager? ActiveSongManager => ActiveSession?.Workspace.Songs;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = MidiPlayer.Instance;

        ApplyWindowPlacement();
        ShowShortcutsInMenus();
        playerScroll.SizeChanged += (_, _) => UpdatePlayerPaneHeight();
        playerDetails.SizeChanged += (_, _) => UpdatePlayerPaneHeight();
        Opened += (_, _) =>
        {
            FitToScreen();
            if (OperatingSystem.IsMacOS()) UseCommandKeyInTooltips(this);
        };
        ApplyStartupView();
        restoringStartupView = false;

        UpdateLibraryDescription();
        lstExtDataSessions.ItemsSource = extDataSessions;

        Logging.OnMessage += LoggingOnMessage;
        MidiPlayer.Instance.PropertyChanged += MidiPlayerOnPropertyChanged;

        _ = RefreshLibraryAsync();
        _ = EnsureCatalogueLoadedAsync();
        UpdateSaveUi();
        UpdateImportLabels();
        UpdateActionStates();
    }

    private const int MaxLogLines = 1000;
    private readonly List<string> logLines = [];

    private void LoggingOnMessage(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (isClosing) return;
            // Bounded, so a save with thousands of warnings does not make every new line slower.
            logLines.Add(message);
            if (logLines.Count > MaxLogLines)
                logLines.RemoveRange(0, logLines.Count - MaxLogLines);
            txtLogView.Text = string.Join(Environment.NewLine, logLines);
            txtLogView.CaretIndex = txtLogView.Text.Length;
            txtStatusMessage.Text = message;
        });
    }

    /// <summary>The least height the karaoke box keeps before the Now Playing pane starts to scroll.</summary>
    private const double KaraokeMinimumHeight = 130;

    // The karaoke row takes whatever height is left, so the grid needs an explicit height inside the scroll viewer.
    private void UpdatePlayerPaneHeight()
    {
        double needed = playerHeader.DesiredSize.Height + playerParts.DesiredSize.Height
            + playerDetails.DesiredSize.Height + KaraokeMinimumHeight;
        playerGrid.Height = Math.Max(playerScroll.Bounds.Height, needed);
    }

    /// <summary>
    /// Shows each menu command's shortcut beside it. The keys themselves are handled in
    /// <see cref="WindowKeyDown"/>; this is only the hint (Cmd on macOS, Ctrl elsewhere).
    /// </summary>
    private void ShowShortcutsInMenus()
    {
        KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        menuOpenSong.InputGesture = new KeyGesture(Key.O, command);
        menuOpenSave.InputGesture = new KeyGesture(Key.O, command | KeyModifiers.Shift);
        menuExportMidi.InputGesture = new KeyGesture(Key.M, command);
        menuImport.InputGesture = new KeyGesture(Key.I, command);
        menuExtract.InputGesture = new KeyGesture(Key.E, command);
        menuDelete.InputGesture = new KeyGesture(Key.Delete);
    }

    private void MidiPlayerOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MidiPlayer.Position) && karaoke.IsEnabled)
            Dispatcher.UIThread.Post(UpdateKaraoke);

        if (e.PropertyName is nameof(MidiPlayer.CanPlay) or nameof(MidiPlayer.IsPlaying))
            Dispatcher.UIThread.Post(UpdateActionStates);
    }

    private async Task PlayLibraryItemAsync(LibrarySong? item)
    {
        if (item is null) return;
        if (!item.CanPreviewSong)
        {
            txtStatusMessage.Text = $"{item.Title} is a BBPV singer package; import it into 3DS Content to use it.";
            return;
        }

        int request = ++libraryPreviewGeneration;
        txtStatusMessage.Text = $"Opening {item.Filename}…";
        bool english = menuEnglishContent.IsChecked;
        try
        {
            // The same copy an import would write, so the karaoke you judge is the karaoke you get.
            (BBPRecord Record, bool EnglishLyrics, string? Problem) loaded = await Task.Run<(BBPRecord, bool, string?)>(() =>
            {
                BBPRecord original = item.LoadRecord();
                if (!english) return (original, false, null);
                try
                {
                    return SongImport.WithEnglishLyrics(original) is BBPRecord withLyrics
                        ? (withLyrics, true, null)
                        : (original, false, null);
                }
                catch (Exception exception) when (exception is InvalidDataException or FormatException)
                {
                    return (original, false, exception.Message);
                }
            });
            if (isClosing || request != libraryPreviewGeneration)
                return;

            PlayItem(loaded.Record, PlaybackSource.Library, englishLyrics: loaded.EnglishLyrics);
            string verb = MidiPlayer.Instance.CanPlay ? "Playing" : "Loaded";
            txtStatusMessage.Text = loaded.Problem is not null
                ? $"{verb} {item.Title}; its English lyrics don't fit this copy, so the original lyrics are shown."
                : loaded.EnglishLyrics
                    ? $"{verb} {loaded.Record.Title} with English lyrics"
                    : $"{verb} {item.Title}";
        }
        catch (Exception exception)
        {
            if (request == libraryPreviewGeneration)
            {
                Logging.Log(exception);
                txtStatusMessage.Text = $"Could not open {item.Filename}: {exception.Message}";
            }
        }
    }

    private void PlayItem(BBPRecord? record, PlaybackSource source, string? sourceName = null, bool englishLyrics = false)
    {
        if (record is null) return;

        currentRecord = record;
        currentPlaybackSource = source;
        currentSaveSession = source == PlaybackSource.SaveData ? ActiveSession : null;
        karaoke = new Karaoke(record.gak);
        BuildKaraokeLines();

        string title = string.IsNullOrWhiteSpace(record.Title) ? record.Filename : record.Title;
        string subtitle = string.IsNullOrWhiteSpace(record.Author)
            ? record.Filename
            : $"{record.Author}  ·  {record.Filename}";

        txtNowPlayingTitle.Text = title;
        txtNowPlayingSubtitle.Text = subtitle;
        txtPlayerTitle.Text = title;
        txtPlayerSubtitle.Text = subtitle;
        txtPlayerSource.Text = source switch
        {
            PlaybackSource.Library => "PREVIEWING FROM LIBRARY",
            PlaybackSource.SaveData => string.IsNullOrWhiteSpace(sourceName)
                ? "PREVIEWING FROM 3DS CONTENT"
                : $"PREVIEWING FROM 3DS CONTENT · {sourceName}",
            PlaybackSource.MovieSaveData => string.IsNullOrWhiteSpace(sourceName)
                ? "PREVIEWING MOVIE TRACK FROM 3DS CONTENT"
                : $"PREVIEWING MOVIE TRACK FROM 3DS CONTENT · {sourceName}",
            _ => "OPENED SONG"
        };

        string format = RecordFormat(record);
        txtPlayerDetails.Text = $"{format} · {record.Instruments} parts · {record.Lines} lines"
            + (record.HasKaraoke ? " · Lyrics" : string.Empty)
            + (record.HasPiano ? " · Piano" : string.Empty)
            + (record.HasGuitar ? " · Guitar" : string.Empty);
        ToolTip.SetTip(txtPlayerDetails, string.IsNullOrWhiteSpace(record.ContentHash) ? txtPlayerDetails.Text : $"{txtPlayerDetails.Text}\nSHA-256  {record.ContentHash}");

        try
        {
            MidiPlayer.Instance.Play(BbpToMidiConverter.Convert(record, CurrentMidiOptions()));
            string origin = source switch
            {
                PlaybackSource.Library => $"library file {record.Filename}",
                PlaybackSource.SaveData => "3DS Content" + (string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $" ({sourceName})"),
                PlaybackSource.MovieSaveData => "movie track in 3DS Content" + (string.IsNullOrWhiteSpace(sourceName) ? string.Empty : $" ({sourceName})"),
                _ => $"opened file {record.Filename}"
            };
            Logging.Log($"{(MidiPlayer.Instance.CanPlay ? "Playing" : "Loaded")} “{title}” from {origin}"
                + (englishLyrics ? ", with English lyrics." : "."));
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
        }

        UpdateActionStates();
        UpdateWindowTitle();
    }

    private static BbpMidiConversionOptions CurrentMidiOptions()
    {
        string customProfile = UserSettingsStore.Current.MidiProfilePath;
        BbpMidiProfile profile = !string.IsNullOrWhiteSpace(customProfile)
            ? BbpMidiProfile.FromFile(customProfile)
            : UserSettingsStore.Current.SoundFontMapping == SoundFontMappingMode.GeneralMidi
                ? BbpMidiProfile.GeneralMidi : BbpMidiProfile.Mosgis;
        return new BbpMidiConversionOptions { Profile = profile };
    }

    private void RefreshCurrentRecordUi()
    {
        if (currentRecord is null) return;

        string title = string.IsNullOrWhiteSpace(currentRecord.Title) ? currentRecord.Filename : currentRecord.Title;
        string subtitle = string.IsNullOrWhiteSpace(currentRecord.Author)
            ? currentRecord.Filename
            : $"{currentRecord.Author}  ·  {currentRecord.Filename}";

        txtNowPlayingTitle.Text = title;
        txtNowPlayingSubtitle.Text = subtitle;
        txtPlayerTitle.Text = title;
        txtPlayerSubtitle.Text = subtitle;
        string format = RecordFormat(currentRecord);
        txtPlayerDetails.Text = $"{format} · {currentRecord.Instruments} parts · {currentRecord.Lines} lines"
            + (currentRecord.HasKaraoke ? " · Lyrics" : string.Empty)
            + (currentRecord.HasPiano ? " · Piano" : string.Empty)
            + (currentRecord.HasGuitar ? " · Guitar" : string.Empty);
        ToolTip.SetTip(txtPlayerDetails, string.IsNullOrWhiteSpace(currentRecord.ContentHash) ? txtPlayerDetails.Text : $"{txtPlayerDetails.Text}\nSHA-256  {currentRecord.ContentHash}");
        UpdateWindowTitle();
    }

    private void UpdateKaraoke()
    {
        karaoke.Position = MidiPlayer.Instance.Position;
        if (!karaoke.IsEnabled || karaokeLines.Count == 0)
            return;

        int lineIndex = FindKaraokeLine(karaoke.PositionStart);
        for (int i = 0; i < karaokeLines.Count; i++)
        {
            KaraokeLineSpan line = karaokeLines[i];
            if (i < lineIndex)
            {
                line.View.SetCompleted();
                continue;
            }
            if (i > lineIndex)
            {
                line.View.SetUpcoming();
                continue;
            }

            int localStart = Math.Clamp(karaoke.PositionStart - line.Start, 0, line.Length);
            int globalEnd = karaoke.PositionStart + karaoke.PositionCount;
            int localEnd = Math.Clamp(globalEnd - line.Start, 0, line.Length);
            line.View.SetCurrent(localStart, localStart, Math.Max(0, localEnd - localStart), karaoke.PositionFraction);
        }

        if (lineIndex != currentKaraokeLine)
        {
            currentKaraokeLine = lineIndex;
            Dispatcher.UIThread.Post(() => CenterKaraokeLine(lineIndex), DispatcherPriority.Loaded);
        }
    }

    private void BuildKaraokeLines()
    {
        karaokeLines.Clear();
        karaokeLinesPanel.Children.Clear();
        currentKaraokeLine = -1;
        karaokeViewer.ScrollToHome();

        if (!karaoke.IsEnabled || string.IsNullOrWhiteSpace(karaoke.Lyrics))
        {
            karaokeLinesPanel.Margin = new Thickness(4, 30);
            karaokeLinesPanel.Children.Add(new TextBlock
            {
                Text = "No karaoke lyrics for this song.",
                Foreground = Avalonia.Media.Brush.Parse("#8A93A0"),
                FontSize = 14,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                Margin = new Thickness(8, 40)
            });
            return;
        }

        karaokeLinesPanel.Margin = new Thickness(4, 80, 4, 110);
        string lyrics = karaoke.Lyrics;
        int start = 0;
        while (start <= lyrics.Length)
        {
            int newline = lyrics.IndexOf('\n', start);
            int end = newline < 0 ? lyrics.Length : newline;
            int length = end - start;
            if (length > 0 && lyrics[start + length - 1] == '\r')
                length--;

            string text = length == 0 ? string.Empty : lyrics.Substring(start, length);
            var view = new KaraokeLineView(text);
            karaokeLines.Add(new KaraokeLineSpan(start, length, view));
            karaokeLinesPanel.Children.Add(view);

            if (newline < 0) break;
            start = newline + 1;
        }

        UpdateKaraoke();
    }

    private int FindKaraokeLine(int position)
    {
        int line = 0;
        for (int i = 1; i < karaokeLines.Count; i++)
        {
            if (position < karaokeLines[i].Start)
                break;
            line = i;
        }
        return line;
    }

    private void CenterKaraokeLine(int lineIndex)
    {
        if ((uint)lineIndex >= (uint)karaokeLines.Count)
            return;

        // BringIntoView is deliberately used for the first Avalonia port. It
        // preserves the useful behaviour without coupling the player to the old desktop player's
        // animated ScrollViewer offsets. Smooth centring can be restored later.
        karaokeLines[lineIndex].View.BringIntoView();
    }

    private void OpenSong(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        BBPRecord record;
        if (ext == ".bbpm")
            record = BbpmPackage.Open(path).LoadSongRecord();
        else if (ext == ".bbp")
            record = BBPRecord.FromBBP(path);
        else if (ext == ".bdx")
            record = BBPRecord.FromBDX(path, UserSettingsStore.Current.BdxOptions);
        else if (ext == ".bbpv")
            throw new InvalidDataException("BBPV is a Vocaloid singer package, not a playable song. Import it into 3DS Content.");
        else
            throw new InvalidDataException($"Unsupported library format: {ext}");

        Logging.Log($"Opened {ext.TrimStart('.').ToUpperInvariant()} {path}");
        rightTabs.SelectedItem = tabPlayer;
        PlayItem(record, PlaybackSource.OpenedFile);
    }

    private async Task LoadSaveDataAsync(string path, bool selectManagerTab = true)
    {
        path = Path.GetFullPath(path);
        ExtDataSession? existing = extDataSessions.FirstOrDefault(session =>
            string.Equals(session.EntryFilePath, path, PathComparison));

        // Reading every song's pack from a slow SD card can take seconds; keep the window responsive.
        if (existing is null)
        {
            existing = await RunBatchAsync("Opening", 1, _ =>
            {
                var opened = new ExtDataSession(path);
                opened.ReloadAll(Logging.Log);
                return opened;
            });
            extDataSessions.Add(existing);
            Logging.Log($"Opened 3DS content {existing.FolderPath}");
        }
        else
        {
            ExtDataSession reopened = existing;
            await RunBatchAsync("Opening", 1, _ =>
            {
                reopened.ReloadAll(Logging.Log);
                return 0;
            });
            Logging.Log($"Refreshed already-open 3DS content {path}");
        }

        lstExtDataSessions.SelectedItem = existing;
        BindActiveExtDataSession();

        if (selectManagerTab)
            rightTabs.SelectedItem = tabSaveManager;

        UpdateActionStates();
    }

    private void SaveAndReload()
    {
        ExtDataSession? session = ActiveSession;
        if (session is null) return;

        session.Workspace.SaveSongs();
        session.ReloadAll(Logging.Log);
        BindActiveExtDataSession();
        rightTabs.SelectedItem = tabSaveManager;
    }

    private void BindActiveExtDataSession()
    {
        ExtDataSession? session = ActiveSession;
        bool loaded = session is not null;

        pnlSaveEmpty.IsVisible = !loaded;
        pnlSaveContent.IsVisible = loaded;
        txtSaveSearch.IsEnabled = loaded;
        lstSaveEditor.IsEnabled = loaded;

        changingExtDataSession = true;
        try
        {
            txtSaveSearch.Text = session?.SearchText ?? string.Empty;
        }
        finally
        {
            changingExtDataSession = false;
        }

        ApplySaveFilter();
        lstSaveMovies.ItemsSource = session?.Movies;
        lstSaveSingers.ItemsSource = session?.Singers;
        UpdateSaveUi();
        UpdateWindowTitle();
        UpdateImportLabels();
        UpdateActionStates();
        ApplyLibraryFilter();
    }

    private void UpdateSaveUi()
    {
        ExtDataSession? session = ActiveSession;
        if (session is null)
        {
            txtSaveDataDescription.Text = "No 3DS content open";
            txtSaveStatus.Text = extDataSessions.Count == 0
                ? "No 3DS content open"
                : $"{extDataSessions.Count} 3DS content set(s) open";
            txtSaveSongsEmpty.IsVisible = false;
            txtSaveCount.Text = "0 selected · 0 songs";
            txtMovieCount.Text = "0 selected · 0 movies";
            txtSingerCount.Text = "0 selected · 0 Vocaloids";
            return;
        }

        int count = session.Songs.Count;
        int shown = lstSaveEditor.ItemsSource?.Cast<object>().Count() ?? 0;
        int selected = lstSaveEditor.SelectedItems?.Count ?? 0;
        int movieSelected = lstSaveMovies.SelectedItems?.Count ?? 0;
        int singerSelected = lstSaveSingers.SelectedItems?.Count ?? 0;
        txtSaveDataDescription.Text = $"{session.FolderPath} · {count:N0} songs · {session.Movies.Count:N0} movies · {session.Singers.Count:N0} Vocaloids";
        txtSaveCount.Text = $"{selected:N0} selected · {shown:N0} shown";
        txtMovieCount.Text = $"{movieSelected:N0} selected · {session.Movies.Count:N0} movies";
        txtSingerCount.Text = $"{singerSelected:N0} selected · {session.Singers.Count:N0} Vocaloids";
        txtSaveSongsEmpty.IsVisible = shown == 0;
        txtSaveSongsEmpty.Text = count > 0
            ? "No songs match your search."
            : "This save has no songs yet. Select songs in the library and press Import.";
        txtSaveStatus.Text = $"3DS content: {session.DisplayName} · {count:N0} songs · {session.Movies.Count:N0} movies · {session.Singers.Count:N0} Vocaloids";
        if (extDataSessions.Count > 1)
            txtSaveStatus.Text += $" · {extDataSessions.Count} open";
    }

    private void UpdateWindowTitle()
    {
        if (ActiveSession is ExtDataSession session)
        {
            Title = $"Degausser — {session.DisplayName}";
            return;
        }

        Title = currentRecord is null
            ? "Degausser"
            : $"Degausser — {(string.IsNullOrWhiteSpace(currentRecord.Title) ? currentRecord.Filename : currentRecord.Title)}";
    }

    private async void OpenSongClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<IStorageFile>? files = await PickerGate.RunAsync(async () => await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Band Brothers file",
                AllowMultiple = false,
                SuggestedStartLocation = await TryGetLibraryFolderAsync(),
                FileTypeFilter =
                [
                    new FilePickerFileType("Band Brothers files") { Patterns = ["*.bbp", "*.bdx", "*.bbpm", "*.bbpv"] },
                    new FilePickerFileType("BBP / BDX songs") { Patterns = ["*.bbp", "*.bdx"] },
                    new FilePickerFileType("BBPM playable movies") { Patterns = ["*.bbpm"] },
                    new FilePickerFileType("BBPV Vocaloid singers") { Patterns = ["*.bbpv"] },
                    FilePickerFileTypes.All
                ]
            }));
            if (files is null || files.Count == 0) return;
            OpenSong(files[0].Path.LocalPath);
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            await MessageDialog.ShowInfoAsync(this, "Could not open song", exception.Message);
        }
    }

    private async void OpenSaveClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<IStorageFolder>? folders = await PickerGate.RunAsync(async () => await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the Band Brothers P save data folder (the one with jb, mv and singer inside)",
                AllowMultiple = false
            }));
            if (folders is not { Count: > 0 } || !folders[0].Path.IsFile) return;
            string folder = folders[0].Path.LocalPath;
            string? manager = ExtDataWorkspace.FindManagerFile(folder);
            if (manager is null)
            {
                await MessageDialog.ShowInfoAsync(this, "Not Band Brothers P save data",
                    "That folder has no jb/mgr.bin in it.\n\nChoose the save data folder: the one that contains the jb, mv and singer folders. With the English patch it is the P folder on the SD card.");
                return;
            }
            await LoadSaveDataAsync(manager);
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            await MessageDialog.ShowInfoAsync(this, "Could not open 3DS content", exception.Message);
        }
    }

    /// <summary>
    /// All workspace I/O runs off the UI thread while a modal progress window
    /// prevents the user from editing the same extdata concurrently. Keep the
    /// complete batch sequential: manager updates depend on preceding imports.
    /// </summary>
    private Task<T> RunBatchAsync<T>(string verb, int total, Func<IProgress<BatchProgress>, T> work)
        => RunBatchAsync(verb, total, (progress, _) => work(progress), cancellable: false);

    /// <summary>
    /// A batch the user can cancel. <paramref name="work"/> checks the token between items, never inside
    /// one, and still runs its closing steps (saving the manager, reloading) after stopping early.
    /// </summary>
    private async Task<T> RunBatchAsync<T>(string verb, int total, Func<IProgress<BatchProgress>, CancellationToken, T> work, bool cancellable = true)
    {
        var dialog = new BatchProgressDialog(verb, total, cancellable && total > 1);
        // Construct on the Avalonia UI thread so progress callbacks return here.
        IProgress<BatchProgress> progress = new Progress<BatchProgress>(dialog.Update);
        CancellationToken cancel = dialog.CancellationToken;
        Task dialogClosed = dialog.ShowDialog(this);
        try
        {
            return await Task.Run(() => work(progress, cancel));
        }
        finally
        {
            dialog.Finish();
            await dialogClosed;
        }
    }

    private async void ImportPackageFileClicked(object? sender, RoutedEventArgs e)
    {
        ExtDataSession? session = ActiveSession;
        if (session is null)
        {
            await MessageDialog.ShowInfoAsync(this, "Import BBPM / BBPV", "Open Band Brothers P 3DS content first.");
            return;
        }

        try
        {
            IReadOnlyList<IStorageFile>? files = await PickerGate.RunAsync(async () => await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import BBPM / BBPV into 3DS content",
                AllowMultiple = true,
                SuggestedStartLocation = await TryGetLibraryFolderAsync(),
                FileTypeFilter =
                [
                    new FilePickerFileType("BBPM / BBPV packages") { Patterns = ["*.bbpm", "*.bbpv"] },
                    new FilePickerFileType("BBPM playable movies") { Patterns = ["*.bbpm"] },
                    new FilePickerFileType("BBPV Vocaloid singers") { Patterns = ["*.bbpv"] },
                    FilePickerFileTypes.All
                ]
            }));
            if (files is null || files.Count == 0) return;

            // Snapshot the UI selection before entering the worker thread.
            string[] paths = files.Select(file => file.Path.LocalPath).ToArray();
            ContentTranslation.Manifest? englishTranslations = menuEnglishContent.IsChecked
                ? ContentTranslation.ReadBundledEnglish() : null;
            var result = await RunBatchAsync("Importing", paths.Length, (progress, cancel) =>
            {
                int imported = 0;
                int failed = 0;
                for (int i = 0; i < paths.Length; i++)
                {
                    if (cancel.IsCancellationRequested) break;
                    string path = paths[i];
                    string name = Path.GetFileName(path);
                    progress.Report(new BatchProgress(i, paths.Length, name, failed));
                    try
                    {
                        string extension = Path.GetExtension(path);
                        if (extension.Equals(".bbpm", StringComparison.OrdinalIgnoreCase))
                            session.Workspace.ImportMoviePackage(path, englishTranslations);
                        else if (extension.Equals(".bbpv", StringComparison.OrdinalIgnoreCase))
                            session.Workspace.ImportSingerPackage(path, englishTranslations);
                        else
                            throw new InvalidDataException($"Unsupported portable package: {extension}");
                        imported++;
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Logging.Log($"Failed to import {name}: {exception.Message}");
                    }
                    progress.Report(new BatchProgress(i + 1, paths.Length, name, failed));
                }
                // Loading the managers can itself take time; keep it off-thread.
                progress.Report(new BatchProgress(paths.Length, paths.Length, "Refreshing 3DS content…", failed));
                session.ReloadAll(Logging.Log);
                return (Imported: imported, Failed: failed, Cancelled: cancel.IsCancellationRequested);
            });

            BindActiveExtDataSession();
            rightTabs.SelectedItem = tabSaveManager;
            Logging.Log(result.Cancelled
                ? $"Cancelled: imported {result.Imported} of {paths.Length} package(s) into 3DS Content" + (result.Failed > 0 ? $"; {result.Failed} failed." : ".")
                : result.Failed == 0
                ? $"Imported all {result.Imported} package(s) into 3DS Content."
                : $"Imported {result.Imported} of {paths.Length} package(s); {result.Failed} failed.");
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            await MessageDialog.ShowInfoAsync(this, "Could not import package", exception.Message);
        }
    }

    private async void RestoreBackupClicked(object? sender, RoutedEventArgs e)
    {
        ExtDataSession? session = ActiveSession;
        if (session is null) return;
        try
        {
            string? snapshot = await new RestoreBackupDialog(session.DisplayName, session.BackupDirectory).ShowDialog<string?>(this);
            if (snapshot is null) return;
            if (MidiPlayer.Instance.IsPlaying)
                MidiPlayer.Instance.IsPlaying = false;

            await RunBatchAsync("Restoring", 1, progress =>
            {
                progress.Report(new BatchProgress(0, 1, Path.GetFileName(snapshot), 0));
                ExtDataBackup.Restore(snapshot, session.Workspace.ExtDataRootDirectory);
                progress.Report(new BatchProgress(1, 1, "Refreshing 3DS content…", 0));
                session.ReloadAll(Logging.Log);
                return 0;
            });
            BindActiveExtDataSession();
            Logging.Log($"Restored {session.DisplayName} from {Path.GetFileName(snapshot)}.");
            await MessageDialog.ShowInfoAsync(this, "Save restored",
                $"{session.DisplayName} is back to how it was at {Path.GetFileName(snapshot)}. Its previous state was kept as another restore point.");
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            try { session.ReloadAll(Logging.Log); BindActiveExtDataSession(); } catch { }
            await MessageDialog.ShowInfoAsync(this, "Could not restore the save", exception.Message);
        }
    }

    private async void TranslateInstalledMoviesClicked(object? sender, RoutedEventArgs e)
    {
        ExtDataSession? session = ActiveSession;
        if (session is null) return;
        try
        {
            if (await MessageDialog.ShowConfirmationAsync(this, "Translate movies in this save?",
                    "Movies that match Degausser's English catalogue get English titles, descriptions and authors, "
                    + "and their songs get English titles and romaji lyrics where available. This rewrites those songs' packs "
                    + "and the movie lists in your 3DS content. A restore point is made first.", "Translate") != DialogChoice.Yes)
                return;

            var manifest = ContentTranslation.ReadBundledEnglish();
            int changed = await RunBatchAsync("Translating", 1, progress =>
            {
                progress.Report(new BatchProgress(0, 1, "Movies", 0));
                int count = session.Workspace.TranslateInstalledMovies(manifest).Count;
                progress.Report(new BatchProgress(1, 1, "Refreshing 3DS content…", 0));
                session.ReloadAll(Logging.Log);
                return count;
            });
            BindActiveExtDataSession();
            Logging.Log($"Translated {changed} movie(s) in 3DS Content.");
            await MessageDialog.ShowInfoAsync(this, "Movies translated", changed == 0
                ? "Nothing to change: no movie matched the catalogue or all of them are already English."
                : $"Translated {changed} movie(s).");
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            await MessageDialog.ShowInfoAsync(this, "Could not translate movies", exception.Message);
        }
    }

    private async void ImportClicked(object? sender, RoutedEventArgs e)
    {
        ExtDataSession? session = ActiveSession;
        // Never enumerate Avalonia selections from a worker thread.
        List<LibrarySong> files = SelectedLibraryFiles();
        if (session is null || files.Count == 0)
            return;

        await ImportLibraryItemsAsync(session, files);
    }

    private async Task ImportLibraryItemsAsync(ExtDataSession session, List<LibrarySong> selected)
    {
        try
        {
            ContentTranslation.Manifest? englishTranslations = menuEnglishContent.IsChecked
                ? ContentTranslation.ReadBundledEnglish() : null;
            bool englishLyrics = menuEnglishContent.IsChecked;
            int withEnglishLyrics = 0;
            var result = await RunBatchAsync("Importing", selected.Count, (progress, cancel) =>
            {
                JbManager jbManager = session.Workspace.Songs;
                JbMgr mgr = jbManager.mgr;
                HashSet<uint> existingIds = session.Workspace.GetLogicalSongIds();
                int imported = 0;
                int failed = 0;
                bool songsChanged = false;

                for (int index = 0; index < selected.Count; index++)
                {
                    // Stop between songs: the manager save below still registers every payload written so far.
                    if (cancel.IsCancellationRequested) break;
                    LibrarySong libraryItem = selected[index];
                    progress.Report(new BatchProgress(index, selected.Count, libraryItem.Filename, failed));
                    try
                    {
                        if (libraryItem.Kind == LibraryItemKind.Bbpm)
                        {
                            BbpmPackage package = BbpmPackage.Open(libraryItem.FullPath);
                            session.Workspace.ImportMoviePackage(libraryItem.FullPath, englishTranslations);
                            existingIds.Add(package.SongId);
                            imported++;
                        }
                        else if (libraryItem.Kind == LibraryItemKind.Bbpv)
                        {
                            session.Workspace.ImportSingerPackage(libraryItem.FullPath, englishTranslations);
                            imported++;
                        }
                        else
                        {
                            BBPRecord record = libraryItem.LoadRecord();
                            SongImport.Added added;
                            try
                            {
                                added = SongImport.Add(mgr, existingIds, record, englishLyrics);
                            }
                            catch (SongImportException exception)
                            {
                                failed++;
                                Logging.Log($"Skipped {libraryItem.Filename}: {exception.Message}");
                                continue;
                            }

                            SongImport.WritePayload(jbManager.Directory, mgr, existingIds, added);
                            if (added.EnglishLyrics) withEnglishLyrics++;
                            songsChanged = true;
                            imported++;
                        }
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Logging.Log($"Failed to import {libraryItem.Filename}: {exception.Message}");
                    }
                    finally
                    {
                        progress.Report(new BatchProgress(index + 1, selected.Count, libraryItem.Filename, failed));
                    }
                }

                if (songsChanged)
                    session.Workspace.SaveSongs();
                progress.Report(new BatchProgress(selected.Count, selected.Count, "Refreshing 3DS content…", failed));
                session.ReloadAll(Logging.Log);
                return (Imported: imported, Failed: failed, Cancelled: cancel.IsCancellationRequested);
            });

            BindActiveExtDataSession();
            rightTabs.SelectedItem = tabSaveManager;
            Logging.Log(result.Cancelled
                ? $"Cancelled: imported {result.Imported} of {selected.Count} selected library item(s) into 3DS Content" + (result.Failed > 0 ? $"; {result.Failed} failed or already existed." : ".")
                : result.Failed == 0
                ? $"Imported all {result.Imported} selected library item(s) into 3DS Content."
                : $"Imported {result.Imported} of {selected.Count} selected library item(s); {result.Failed} failed or already existed.");
            if (withEnglishLyrics > 0)
                Logging.Log($"{withEnglishLyrics} song(s) were given English lyrics from the scorefix folder.");
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            await MessageDialog.ShowInfoAsync(this, "Could not import library items", exception.Message);
        }
    }

    private async void ExtractClicked(object? sender, RoutedEventArgs e)
    {
        ExtDataSession? session = ActiveSession;
        if (session is null) return;

        // Capture selection and tab on the dispatcher; never use Avalonia
        // controls inside the background export loop.
        var movies = pnlSaveContent.SelectedItem == tabSaveMovies
            ? lstSaveMovies.SelectedItems?.Cast<ExtDataWorkspace.MovieEntry>().ToList() ?? [] : [];
        var singers = pnlSaveContent.SelectedItem == tabSaveSingers
            ? lstSaveSingers.SelectedItems?.Cast<ExtDataWorkspace.SingerEntry>().ToList() ?? [] : [];
        var songs = pnlSaveContent.SelectedItem != tabSaveMovies && pnlSaveContent.SelectedItem != tabSaveSingers
            ? lstSaveEditor.SelectedItems?.Cast<BBPRecord>().ToList() ?? [] : [];
        int requested = movies.Count + singers.Count + songs.Count;
        if (requested == 0) return;

        string extractedDirectory = Path.Combine(UserSettingsStore.Current.LibraryPath, "Extracted");
        try
        {
            var result = await RunBatchAsync("Extracting", requested, (progress, cancel) =>
            {
                Directory.CreateDirectory(extractedDirectory);
                int extracted = 0;
                int failed = 0;
                int processed = 0;
                var exportedLibraryItems = new List<LibrarySong>();

                foreach (var movie in movies)
                {
                    if (cancel.IsCancellationRequested) break;
                    string label = movie.MovieTitle;
                    progress.Report(new BatchProgress(processed, requested, label, failed));
                    try
                    {
                        string baseName = $"{movie.SongTitle} ({movie.Author})".IOFriendly();
                        if (string.IsNullOrWhiteSpace(baseName)) baseName = $"movie-{movie.VideoId:x8}";
                        string path = GetUniquePath(Path.Combine(extractedDirectory, baseName + ".bbpm"));
                        session.Workspace.ExportMoviePackage(movie, path);
                        exportedLibraryItems.Add(LibrarySong.Read(path));
                        extracted++;
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Logging.Log($"Failed to extract movie {movie.VideoId:X8} ({movie.MovieTitle}): {exception.Message}");
                    }
                    finally
                    {
                        progress.Report(new BatchProgress(++processed, requested, label, failed));
                    }
                }
                foreach (var singer in singers)
                {
                    if (cancel.IsCancellationRequested) break;
                    string label = singer.Artist;
                    progress.Report(new BatchProgress(processed, requested, label, failed));
                    try
                    {
                        string baseName = singer.Artist.IOFriendly();
                        if (string.IsNullOrWhiteSpace(baseName)) baseName = $"singer-{singer.Slot:0000}";
                        string path = GetUniquePath(Path.Combine(extractedDirectory, baseName + ".bbpv"));
                        session.Workspace.ExportSingerPackage(singer, path);
                        exportedLibraryItems.Add(LibrarySong.Read(path));
                        extracted++;
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Logging.Log($"Failed to extract singer slot {singer.Slot} ({singer.Artist}): {exception.Message}");
                    }
                    finally
                    {
                        progress.Report(new BatchProgress(++processed, requested, label, failed));
                    }
                }
                foreach (BBPRecord record in songs)
                {
                    if (cancel.IsCancellationRequested) break;
                    string label = record.Title;
                    progress.Report(new BatchProgress(processed, requested, label, failed));
                    try
                    {
                        string authorSuffix = string.IsNullOrWhiteSpace(record.Author) ? string.Empty : $" ({record.Author})";
                        string baseName = $"{record.Title}{authorSuffix}".IOFriendly();
                        if (string.IsNullOrWhiteSpace(baseName)) baseName = $"slot-{record.Slot}";
                        string path = GetUniquePath(Path.Combine(extractedDirectory, $"{baseName}.bbp"));
                        record.SaveAsBBPFile(path);
                        exportedLibraryItems.Add(LibrarySong.Read(path));
                        extracted++;
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Logging.Log($"Failed to extract slot {record.Slot} ({record.Title}): {exception.Message}");
                    }
                    finally
                    {
                        progress.Report(new BatchProgress(++processed, requested, label, failed));
                    }
                }
                return (Extracted: extracted, Items: exportedLibraryItems, Cancelled: cancel.IsCancellationRequested);
            });

            // Observable UI state is updated only after the worker completes.
            librarySongs.AddRange(result.Items);
            Logging.Log($"{(result.Cancelled ? "Cancelled: extracted" : "Extracted")} {result.Extracted} of {requested} selected item(s) to {extractedDirectory}");
            ApplyLibraryFilter();
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            await MessageDialog.ShowInfoAsync(this, "Could not extract items", exception.Message);
        }
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private async void ApplyScorefixClicked(object? sender, RoutedEventArgs e)
    {
        if (currentRecord is null) return;

        string? text = await new ScorefixDialog(ScorefixLibrary.Current.Find(currentRecord), Scorefix.Template(currentRecord)).ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            ScorefixDocument applied = Scorefix.Apply(currentRecord, text);
            karaoke = new Karaoke(currentRecord.gak);
            BuildKaraokeLines();
            RefreshCurrentRecordUi();

            if (currentPlaybackSource == PlaybackSource.SaveData && currentSaveSession is not null)
            {
                JbManager manager = currentSaveSession.Workspace.Songs;
                // The preview can outlive its slot (song deleted, save reloaded or re-imported);
                // never write over whatever sits there now.
                if (currentRecord.Slot < 0 || currentRecord.Slot >= manager.mgr.items.Length
                    || manager.mgr.items[currentRecord.Slot].titleID != currentRecord.mgrItem.titleID)
                {
                    await MessageDialog.ShowInfoAsync(this, "Scorefix not saved",
                        "This song is no longer in the 3DS save at the position it was opened from, so nothing was written. Open it from the save again to apply the scorefix.");
                    return;
                }
                manager.mgr.items[currentRecord.Slot] = currentRecord.mgrItem;
                currentSaveSession.Workspace.CreateRestorePoint();
                currentSaveSession.Workspace.PreserveFile(currentRecord.FullPath);
                currentRecord.SaveAsPackFile(currentRecord.FullPath);
                currentSaveSession.Workspace.SaveSongs();

                // Rebinding is enough to refresh the title in the manager list;
                // do not reload records or leave Now Playing.
                if (ReferenceEquals(ActiveSession, currentSaveSession))
                    ApplySaveFilter();

                txtStatusMessage.Text = $"Applied scorefix to {applied.FlatTitle} and saved it to 3DS Content.";
                Logging.Log(txtStatusMessage.Text);
            }
            else
            {
                txtStatusMessage.Text = $"Applied scorefix to {applied.FlatTitle} for this preview (not written to the source file).";
                Logging.Log(txtStatusMessage.Text);
            }
        }
        catch (Exception exception)
        {
            Logging.Log($"Scorefix rejected: {exception.Message}");
            await MessageDialog.ShowInfoAsync(this, "Could not apply scorefix", exception.Message);
        }
    }

    private async void ExportMidiClicked(object? sender, RoutedEventArgs e)
    {
        if (currentRecord is null) return;

        try
        {
            IStorageFile? file = await PickerGate.RunAsync(async () => await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export MIDI",
                DefaultExtension = "mid",
                SuggestedFileName = $"{currentRecord.Title}.mid".IOFriendly(),
                FileTypeChoices = [new FilePickerFileType("MIDI") { Patterns = ["*.mid", "*.midi"] }]
            }));
            if (file is null) return;

            BbpMidiConversionOptions options = CurrentMidiOptions();
            // UTF-8 is Degausser's chosen release default. Shift-JIS may be
            // requested via a custom JSON profile for older Japanese tooling.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding encoding = UserSettingsStore.Current.MidiTextEncoding.Equals("shift-jis", StringComparison.OrdinalIgnoreCase)
                ? Encoding.GetEncoding(932) : Encoding.UTF8;
            BbpToMidiConverter.Convert(currentRecord, options)
                .File.Write(file.Path.LocalPath, overwriteFile: true,
                    settings: new WritingSettings { TextEncoding = encoding });
            Logging.Log($"Exported MIDI to {file.Path.LocalPath}");
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            await MessageDialog.ShowInfoAsync(this, "Could not export MIDI", exception.Message);
        }
    }

    private void RefreshClicked(object? sender, RoutedEventArgs e) => _ = RefreshLibraryAsync();

    private async Task RefreshLibraryAsync()
    {
        libraryScanCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        libraryScanCancellation = cancellation;
        int generation = ++libraryScanGeneration;
        string libraryPath = Path.GetFullPath(UserSettingsStore.Current.LibraryPath);

        bool changingFolder = loadedLibraryPath is not null
            && !string.Equals(loadedLibraryPath, libraryPath, PathComparison);
        if (changingFolder)
        {
            librarySongs = [];
            ApplyLibraryFilter();
        }

        UpdateLibraryDescription();
        ShowLibraryScanProgress("Finding BBP, BDX, BBPM and BBPV files…", indeterminate: true);
        txtStatusMessage.Text = "Finding songs in library…";

        var progress = new Progress<LibraryScanProgress>(value =>
        {
            if (generation != libraryScanGeneration || isClosing)
                return;

            if (value.IsEnumerating)
            {
                ShowLibraryScanProgress("Finding BBP, BDX, BBPM and BBPV files…", indeterminate: true);
                return;
            }

            int total = Math.Max(1, value.Total);
            if (!prgLibraryScan.IsIndeterminate && value.Completed < prgLibraryScan.Value)
                return;

            prgLibraryScan.IsIndeterminate = false;
            prgLibraryScan.Maximum = total;
            prgLibraryScan.Value = Math.Min(value.Completed, total);
            txtLibraryScanProgress.Text = value.Failed == 0
                ? $"Reading metadata… {value.Completed:N0} / {value.Total:N0}"
                : $"Reading metadata… {value.Completed:N0} / {value.Total:N0} · {value.Failed:N0} skipped";
            txtStatusMessage.Text = $"Scanning library: {value.Completed:N0} / {value.Total:N0}";
        });

        try
        {
            LibraryScanResult result = await LibraryScanner.ScanAsync(libraryPath, progress, cancellation.Token);
            if (cancellation.IsCancellationRequested || generation != libraryScanGeneration || isClosing)
                return;

            librarySongs = result.Songs;
            loadedLibraryPath = libraryPath;
            ApplyLibraryFilter();

            string elapsed = result.Elapsed.TotalSeconds < 1
                ? $"{result.Elapsed.TotalMilliseconds:N0} ms"
                : $"{result.Elapsed.TotalSeconds:N1} s";
            string skipped = result.Failed == 0 ? string.Empty : $" · {result.Failed:N0} skipped";
            Logging.Log($"Library scan complete: {result.Songs.Count:N0} song(s){skipped} from {libraryPath} in {elapsed}");
        }
        catch (OperationCanceledException)
        {
            if (generation == libraryScanGeneration && !isClosing)
                txtStatusMessage.Text = "Library scan cancelled";
        }
        catch (Exception exception)
        {
            if (generation == libraryScanGeneration && !isClosing)
            {
                Logging.Log(exception);
                txtStatusMessage.Text = $"Could not read the library folder: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(libraryScanCancellation, cancellation))
            {
                libraryScanCancellation = null;
                HideLibraryScanProgress();
                UpdateActionStates();
            }
            cancellation.Dispose();
        }
    }

    private void ShowLibraryScanProgress(string message, bool indeterminate)
    {
        pnlLibraryScan.IsVisible = true;
        txtLibraryEmpty.IsVisible = false;
        txtLibraryScanProgress.Text = message;
        prgLibraryScan.IsIndeterminate = indeterminate;
        if (indeterminate)
            prgLibraryScan.Value = 0;
    }

    private void HideLibraryScanProgress()
    {
        pnlLibraryScan.IsVisible = false;
        prgLibraryScan.IsIndeterminate = false;
        prgLibraryScan.Value = 0;
        UpdateLibraryEmptyState(lstMediaLibrary.ItemsSource is System.Collections.ICollection items ? items.Count : 0);
    }

    private void CancelLibraryScan(object? sender, RoutedEventArgs e)
        => libraryScanCancellation?.Cancel();

    private static readonly Avalonia.Media.Geometry PlayIcon = Avalonia.Media.Geometry.Parse("M0,0 L10,6 L0,12 Z");
    private static readonly Avalonia.Media.Geometry PauseIcon = Avalonia.Media.Geometry.Parse("M0,0 H3.5 V12 H0 Z M6.5,0 H10 V12 H6.5 Z");

    private void PlayPauseClicked(object? sender, RoutedEventArgs e)
    {
        if (!MidiPlayer.Instance.CanPlay) return;
        if (MidiPlayer.Instance.IsPlaying)
            MidiPlayer.Instance.IsPlaying = false;
        else
        {
            MidiPlayer.Instance.Play();
        }
        UpdateActionStates();
    }

    private async void DeleteClicked(object? sender, RoutedEventArgs e)
        => await DeleteAsync();

    private async Task DeleteAsync()
    {
        JbManager? jbManager = ActiveSongManager;
        if (jbManager is null || lstSaveEditor.SelectedItems is null || lstSaveEditor.SelectedItems.Count == 0)
            return;

        int selectedCount = lstSaveEditor.SelectedItems.Count;
        string noun = selectedCount == 1 ? "song" : "songs";
        DialogChoice confirmation = await MessageDialog.ShowConfirmationAsync(
            this,
            "Delete from 3DS save",
            $"Delete {selectedCount} selected {noun} from this 3DS save?\n\nA backup of the save's song lists and the removed song data is kept, and can be restored from the 3DS Content menu.",
            "Delete");
        if (confirmation != DialogChoice.Yes)
            return;

        // Take the songs out of the manager and write it first; only then remove their files
        // (copied into the restore point), so a failed write can't leave entries without files.
        var removed = new List<uint>();
        foreach (BBPRecord record in lstSaveEditor.SelectedItems.Cast<BBPRecord>().ToList())
        {
            removed.Add(record.mgrItem.titleID);
            jbManager.mgr.items[record.Slot] = JbMgr.Item.Empty;
        }

        ExtDataSession? session = ActiveSession;
        try
        {
            SaveAndReload();
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
            // Nothing was written, so put the in-memory manager back to match the disk.
            session?.ReloadAll(Logging.Log);
            BindActiveExtDataSession();
            await MessageDialog.ShowInfoAsync(this, "Could not update the save",
                $"Degausser could not finish updating the save: {exception.Message}\n\nThe song list was reloaded from disk. If this keeps happening, restore a backup from the 3DS Content menu.");
            return;
        }

        int notRemoved = session?.Workspace.RemoveSongPayloads(removed, Logging.Log) ?? 0;
        int deleted = removed.Count;
        Logging.Log($"Deleted {deleted} of {selectedCount} selected song(s) from the 3DS save.");
        if (notRemoved > 0)
            await MessageDialog.ShowInfoAsync(this, "Some files were left behind",
                $"The songs were removed from the save, but {notRemoved} song data folder(s) could not be deleted. They are harmless and can be removed by hand; see the Activity log for the paths.");
    }

    private void SaveSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (!changingExtDataSession && ActiveSession is ExtDataSession session)
            session.SearchText = txtSaveSearch.Text ?? string.Empty;
        ApplySaveFilter();
    }

    private void ApplySaveFilter()
    {
        ExtDataSession? session = ActiveSession;
        if (session is null)
        {
            lstSaveEditor.ItemsSource = null;
            UpdateSaveUi();
            return;
        }

        string query = (txtSaveSearch.Text ?? string.Empty).Trim();
        List<BBPRecord> filtered = session.Songs.Where(record =>
            LibrarySong.MatchesManagerItem(
                record.mgrItem,
                query,
                record.Slot.ToString(CultureInfo.InvariantCulture))).ToList();
        lstSaveEditor.ItemsSource = filtered;
        UpdateSaveUi();
    }

    private async void PlaySelectedLibrary(object? sender, RoutedEventArgs e)
        => await PlayLibraryRowAsync(lstMediaLibrary.SelectedItem as LibraryRow);

    private void PlaySelectedSave(object? sender, RoutedEventArgs e)
        => PlayItem(lstSaveEditor.SelectedItem as BBPRecord, PlaybackSource.SaveData, ActiveSession?.DisplayName);

    private async void LibraryDoubleTapped(object? sender, RoutedEventArgs e)
        => await PlayLibraryRowAsync(lstMediaLibrary.SelectedItem as LibraryRow);

    private void SaveDoubleTapped(object? sender, RoutedEventArgs e)
        => PlayItem(lstSaveEditor.SelectedItem as BBPRecord, PlaybackSource.SaveData, ActiveSession?.DisplayName);

    private void SaveContentTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Avalonia can raise the nested TabControl's initial SelectionChanged while
        // InitializeComponent() is still constructing the XAML namescope.  At that
        // point generated x:Name fields (for example txtSaveDataDescription) have
        // not all been assigned yet.  The constructor performs the real UI refresh
        // immediately after startup-view restoration, so ignore this early event.
        if (restoringStartupView)
            return;

        UpdateSaveUi();
        UpdateImportLabels();
        UpdateActionStates();
    }

    private void MovieSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSaveUi();
        UpdateActionStates();
    }

    private void SingerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSaveUi();
        UpdateActionStates();
    }

    private void MovieDoubleTapped(object? sender, RoutedEventArgs e) => PlaySelectedMovie(sender, e);

    private void PlaySelectedMovie(object? sender, RoutedEventArgs e)
    {
        if (ActiveSession is not ExtDataSession session
            || lstSaveMovies.SelectedItem is not ExtDataWorkspace.MovieEntry movie)
            return;

        try
        {
            if (!File.Exists(movie.PackPath))
                throw new FileNotFoundException("Movie-linked song pack is missing.", movie.PackPath);

            BBPRecord record = session.Workspace.LoadMovieSong(movie);
            PlayItem(record, PlaybackSource.MovieSaveData, session.DisplayName);
            rightTabs.SelectedItem = tabPlayer;
            txtStatusMessage.Text = MidiPlayer.Instance.CanPlay
                ? $"Playing movie track: {movie.SongTitle}"
                : $"Loaded movie track: {movie.SongTitle}";
        }
        catch (Exception exception)
        {
            Logging.Log($"Could not preview movie track {movie.SongId:X8}: {exception.Message}");
        }
    }

    private static string RecordFormat(BBPRecord record)
    {
        string extension = Path.GetExtension(record.FullPath);
        return extension.ToLowerInvariant() switch
        {
            ".bdx" => "BDX",
            ".bbpm" => "BBPM",
            _ => "BBP"
        };
    }

    private void LibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateLibraryCount();
        UpdateImportLabels();
        UpdateActionStates();
    }

    private void SaveSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSaveUi();
        UpdateActionStates();
    }

    private void UpdateImportLabels()
    {
        string target = ActiveSession?.DisplayName ?? "3DS Content";
        string label = $"Import to {target}";
        List<LibrarySong> files = SelectedLibraryFiles();
        if (files.Count == 1 && files[0] is LibrarySong record)
        {
            label = record.Kind switch
            {
                LibraryItemKind.Bdx => $"Convert & Import to {target}",
                LibraryItemKind.Bbpm => $"Import BBPM to {target}",
                LibraryItemKind.Bbpv => $"Import BBPV to {target}",
                _ => $"Import to {target}"
            };
        }

        txtImportButtonLabel.Text = label;
        txtToolbarImportLabel.Text = label;
        txtExtractButtonLabel.Text = ActiveSession is null
            ? "←  Extract to Library"
            : $"←  Extract from {target}";
        txtToolbarExtractLabel.Text = ActiveSession is null
            ? "Extract to Library"
            : $"Extract from {target}";
    }

    private void ExtDataSessionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (changingExtDataSession) return;
        BindActiveExtDataSession();
    }

    private void CloseExtDataSession(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExtDataSession session)
            return;

        int index = extDataSessions.IndexOf(session);
        extDataSessions.Remove(session);

        changingExtDataSession = true;
        try
        {
            if (extDataSessions.Count > 0)
                lstExtDataSessions.SelectedIndex = Math.Min(index, extDataSessions.Count - 1);
            else
                lstExtDataSessions.SelectedItem = null;
        }
        finally
        {
            changingExtDataSession = false;
        }

        BindActiveExtDataSession();
        e.Handled = true;
    }

    private void RightTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (restoringStartupView)
            return;

        if (rightTabs?.SelectedItem == tabPlayer)
            UserSettingsStore.Current.LastView = MainViewPreference.NowPlaying;
        else if (rightTabs?.SelectedItem == tabSaveManager)
            UserSettingsStore.Current.LastView = MainViewPreference.ThreeDsContent;

        UpdateActionStates();
    }

    private void ApplyStartupView()
    {
        UserSettings settings = UserSettingsStore.Current;
        MainViewPreference view = settings.StartupView switch
        {
            StartupViewPreference.NowPlaying => MainViewPreference.NowPlaying,
            StartupViewPreference.ThreeDsContent => MainViewPreference.ThreeDsContent,
            _ => settings.LastView
        };
        rightTabs.SelectedItem = view == MainViewPreference.NowPlaying ? tabPlayer : tabSaveManager;
    }

    // The 1440x900 default (or a size saved on a bigger monitor) can exceed a laptop screen.
    private void FitToScreen()
    {
        if (WindowState != WindowState.Normal || Screens.ScreenFromWindow(this) is not { } screen)
            return;
        double scale = screen.Scaling;
        double maxWidth = screen.WorkingArea.Width / scale;
        double maxHeight = screen.WorkingArea.Height / scale;
        if (Width <= maxWidth && Height <= maxHeight)
            return;
        Width = Math.Max(Math.Min(MinWidth, maxWidth), Math.Min(Width, maxWidth));
        Height = Math.Max(Math.Min(MinHeight, maxHeight), Math.Min(Height, maxHeight));
        Position = new PixelPoint(screen.WorkingArea.X, screen.WorkingArea.Y);
    }

    private static void UseCommandKeyInTooltips(Avalonia.LogicalTree.ILogical root)
    {
        if (root is Control control && ToolTip.GetTip(control) is string tip && tip.Contains("Ctrl+"))
            ToolTip.SetTip(control, tip.Replace("Ctrl+", "Cmd+"));
        foreach (Avalonia.LogicalTree.ILogical child in root.LogicalChildren)
            UseCommandKeyInTooltips(child);
    }

    private void ApplyWindowPlacement()
    {
        UserSettings settings = UserSettingsStore.Current;
        if (!settings.RememberWindowPlacement)
            return;

        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);

        if (settings.WindowLeft is double left && settings.WindowTop is double top)
        {
            var point = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
            if (Screens.ScreenFromPoint(point) is not null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = point;
            }
        }

        if (settings.WindowMaximized)
            Opened += (_, _) => WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        UserSettings settings = UserSettingsStore.Current;
        if (settings.RememberWindowPlacement)
        {
            settings.WindowMaximized = WindowState == WindowState.Maximized;
            if (!settings.WindowMaximized)
            {
                settings.WindowWidth = Math.Max(MinWidth, Width);
                settings.WindowHeight = Math.Max(MinHeight, Height);
                settings.WindowLeft = Position.X;
                settings.WindowTop = Position.Y;
            }
        }
        UserSettingsStore.Save();
    }

    private void UpdateLibraryDescription()
    {
        if (txtLibraryDescription is null) return;
        txtLibraryDescription.Text = UserSettingsStore.Current.LibraryPath;
        ToolTip.SetTip(txtLibraryDescription, UserSettingsStore.Current.LibraryPath);
    }

    private async void ShowSettingsClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow();
        bool saved = await dialog.ShowDialog<bool>(this);
        if (!saved) return;

        UpdateLibraryDescription();
        MidiPlayer.Instance.ReloadPlaybackSettings();
        if (dialog.ScorefixPathChanged)
            ReloadScorefixes();
        if (dialog.LibraryPathChanged)
            _ = RefreshLibraryAsync();

    }

    private async void ShowAboutClicked(object? sender, RoutedEventArgs e)
        => await new AboutWindow().ShowDialog(this);

    private void ExitClicked(object? sender, RoutedEventArgs e) => Close();

    private async void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Cmd on macOS, Ctrl elsewhere.
        KeyModifiers commandKey = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        bool ctrl = e.KeyModifiers.HasFlag(commandKey);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && shift && e.Key == Key.O)
        {
            OpenSaveClicked(sender, e);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.O)
        {
            OpenSongClicked(sender, e);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.I)
        {
            ImportClicked(sender, e);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.E)
        {
            ExtractClicked(sender, e);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.M)
        {
            ExportMidiClicked(sender, e);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.R)
        {
            _ = RefreshLibraryAsync();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.F)
        {
            await FocusCatalogueSearchAsync();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.F)
        {
            txtLibrarySearch.Focus();
            txtLibrarySearch.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.P)
        {
            PlayPauseClicked(sender, e);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D1)
        {
            rightTabs.SelectedItem = tabSaveManager;
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D2)
        {
            rightTabs.SelectedItem = tabPlayer;
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D3)
        {
            rightTabs.SelectedItem = tabActivity;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && ActiveSession is not null
            && pnlSaveContent.SelectedItem == tabSaveSongs
            && (lstSaveEditor.SelectedItems?.Count ?? 0) > 0)
        {
            await DeleteAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (lstMediaLibrary.IsKeyboardFocusWithin)
            {
                await PlayLibraryRowAsync(lstMediaLibrary.SelectedItem as LibraryRow);
                e.Handled = true;
            }
            else if (lstSaveEditor.IsKeyboardFocusWithin)
            {
                PlayItem(lstSaveEditor.SelectedItem as BBPRecord, PlaybackSource.SaveData, ActiveSession?.DisplayName);
                e.Handled = true;
            }
            else if (lstSaveMovies.IsKeyboardFocusWithin)
            {
                PlaySelectedMovie(sender, e);
                e.Handled = true;
            }
        }
    }

    private void UpdateActionStates()
    {
        bool canImport = ActiveSession is not null && SelectedLibraryFiles().Count > 0;
        bool songTab = pnlSaveContent?.SelectedItem == tabSaveSongs;
        bool movieTab = pnlSaveContent?.SelectedItem == tabSaveMovies;
        bool singerTab = pnlSaveContent?.SelectedItem == tabSaveSingers;
        bool canExtract = ActiveSession is not null && (
            songTab ? (lstSaveEditor?.SelectedItems?.Count ?? 0) > 0 :
            movieTab ? (lstSaveMovies?.SelectedItems?.Count ?? 0) > 0 :
            singerTab && (lstSaveSingers?.SelectedItems?.Count ?? 0) > 0);
        bool canDelete = ActiveSession is not null && songTab && (lstSaveEditor?.SelectedItems?.Count ?? 0) > 0;
        bool canMidi = currentRecord is not null;
        bool canPlay = currentRecord is not null && MidiPlayer.Instance.CanPlay;
        bool canPreviewLibrary = lstMediaLibrary?.SelectedItems?.Count == 1
            && lstMediaLibrary.SelectedItem is LibraryRow { CanPreview: true };

        menuImport.IsEnabled = btnToolbarImport.IsEnabled = btnImport.IsEnabled = canImport;
        menuImportPackageFile.IsEnabled = menuTranslateInstalledMovies.IsEnabled = menuRestoreBackup.IsEnabled = ActiveSession is not null;
        menuExtract.IsEnabled = btnToolbarExtract.IsEnabled = btnExtract.IsEnabled = canExtract;
        menuDelete.IsEnabled = btnDelete.IsEnabled = canDelete;
        menuExportMidi.IsEnabled = btnToolbarMidi.IsEnabled = btnPlayerMidi.IsEnabled = canMidi;
        btnScorefix.IsEnabled = canMidi;
        btnLibraryPreview.IsEnabled = canPreviewLibrary;
        btnSavePreview.IsEnabled = lstSaveEditor?.SelectedItems?.Count == 1 && lstSaveEditor.SelectedItem is BBPRecord;
        btnMoviePreview.IsEnabled = lstSaveMovies?.SelectedItems?.Count == 1;
        btnPlayPause.IsEnabled = canPlay;
        // The button says what a click will do.
        bool playing = MidiPlayer.Instance.IsPlaying;
        txtPlayPauseLabel.Text = playing ? "Pause" : "Play";
        icoPlayPause.Data = playing ? PauseIcon : PlayIcon;

    }

    private async Task<IStorageFolder?> TryGetLibraryFolderAsync()
    {
        try
        {
            string path = UserSettingsStore.Current.LibraryPath;
            if (!Directory.Exists(path)) return null;
            return await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(path)));
        }
        catch
        {
            return null;
        }
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    protected override void OnClosed(EventArgs e)
    {
        isClosing = true;
        libraryScanCancellation?.Cancel();
        Logging.OnMessage -= LoggingOnMessage;
        MidiPlayer.Instance.PropertyChanged -= MidiPlayerOnPropertyChanged;
        SaveWindowPlacement();

        var stopwatch = Stopwatch.StartNew();
        Logging.Log("Closing MIDI playback…");
        MidiPlayer.Instance.Close();
        stopwatch.Stop();
        Logging.Log($"MIDI playback closed in {stopwatch.ElapsedMilliseconds:N0} ms.");

        base.OnClosed(e);
    }
}
