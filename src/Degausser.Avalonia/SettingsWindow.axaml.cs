using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Degausser;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        UserSettings settings = UserSettingsStore.Current;
        txtLibraryPath.Text = settings.LibraryPath;
        txtSoundFontPath.Text = settings.SoundFontPath;
        txtScorefixPath.Text = settings.ScorefixPath;
        UpdateScorefixStatus();
        cmbSoundFontMapping.SelectedIndex = settings.SoundFontMapping == SoundFontMappingMode.GeneralMidi ? 1 : 0;
        txtBdxAuthor.Text = settings.BdxDefaultAuthor;
        chkRememberWindow.IsChecked = settings.RememberWindowPlacement;
        txtMidiProfilePath.Text = settings.MidiProfilePath;
        cmbMidiTextEncoding.SelectedIndex = settings.MidiTextEncoding.Equals("shift-jis", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        chkRememberSettings.IsChecked = UserSettingsStore.RememberSettings;
        txtSettingsPath.Text = UserSettingsStore.SettingsDirectory;
        cmbStartupView.SelectedIndex = settings.StartupView switch
        {
            StartupViewPreference.NowPlaying => 1,
            StartupViewPreference.ThreeDsContent => 2,
            _ => 0
        };

    }

    public bool LibraryPathChanged { get; private set; }
    public bool ScorefixPathChanged { get; private set; }

    private void UpdateScorefixStatus()
    {
        string path = (txtScorefixPath.Text ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            txtScorefixStatus.Text = "No folder chosen. Unpack the Scorefixes download as a “scorefixes” folder beside Degausser, or pick it here.";
            return;
        }
        if (!Directory.Exists(path))
        {
            txtScorefixStatus.Text = "That folder doesn't exist.";
            return;
        }
        ScorefixLibrary library = ScorefixLibrary.FromDirectory(path);
        txtScorefixStatus.Text = library.Count == 0
            ? "No scorefix .txt files found in that folder."
            : $"{library.Count} scorefix(es) found." + (library.Problems.Count > 0 ? $" {library.Problems.Count} file(s) skipped: {library.Problems[0]}" : string.Empty);
    }

    private void ScorefixPathLostFocus(object? sender, RoutedEventArgs e) => UpdateScorefixStatus();

    private async void BrowseScorefixes(object? sender, RoutedEventArgs e)
    {
        IStorageFolder? start = null;
        if (Directory.Exists(txtScorefixPath.Text))
            start = await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(txtScorefixPath.Text)));

        IReadOnlyList<IStorageFolder>? folders = await PickerGate.RunAsync(async () => await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder of scorefix .txt files",
            AllowMultiple = false,
            SuggestedStartLocation = start
        }));

        if (folders is { Count: > 0 } && folders[0].Path.IsFile)
        {
            txtScorefixPath.Text = folders[0].Path.LocalPath;
            UpdateScorefixStatus();
        }
    }

    private async void BrowseLibrary(object? sender, RoutedEventArgs e)
    {
        IStorageFolder? start = null;
        if (Directory.Exists(txtLibraryPath.Text))
            start = await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(txtLibraryPath.Text)));

        IReadOnlyList<IStorageFolder>? folders = await PickerGate.RunAsync(async () => await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose your Band Brothers library folder",
            AllowMultiple = false,
            SuggestedStartLocation = start
        }));

        if (folders is { Count: > 0 } && folders[0].Path.IsFile)
            txtLibraryPath.Text = folders[0].Path.LocalPath;
    }


    private async void BrowseSoundFont(object? sender, RoutedEventArgs e)
    {
        IStorageFolder? start = null;
        string current = (txtSoundFontPath.Text ?? string.Empty).Trim();
        if (File.Exists(current))
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(current));
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                start = await StorageProvider.TryGetFolderFromPathAsync(new Uri(directory));
        }

        IReadOnlyList<IStorageFile>? files = await PickerGate.RunAsync(async () => await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a SoundFont for preview playback",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter =
            [
                new FilePickerFileType("SoundFont 2") { Patterns = ["*.sf2"] },
                FilePickerFileTypes.All
            ]
        }));

        if (files is { Count: > 0 } && files[0].Path.IsFile)
            txtSoundFontPath.Text = files[0].Path.LocalPath;
    }

    // musical-artifacts.com sits behind a Cloudflare challenge, so the file can't be fetched from code:
    // open the page and let the user download it and pick it with Browse.
    private async void DownloadDefaultSoundFont(object? sender, RoutedEventArgs e)
    {
        const string url = "https://musical-artifacts.com/artifacts/1657";
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri(url)))
                throw new InvalidOperationException("The browser could not be opened.");
        }
        catch (Exception error)
        {
            await MessageDialog.ShowInfoAsync(this, "Get BBP.sf2", $"Download BBP.sf2 from {url}, then choose it with Browse.\n\n{error.Message}");
        }
    }

    private async void SaveSettings(object? sender, RoutedEventArgs e)
    {
        string path = (txtLibraryPath.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            await MessageDialog.ShowInfoAsync(this, "Degausser Settings", "Choose a library folder.");
            return;
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception exception)
        {
            await MessageDialog.ShowInfoAsync(this, "Degausser Settings", $"That library folder cannot be used.\n\n{exception.Message}");
            return;
        }

        string scorefixPath = (txtScorefixPath.Text ?? string.Empty).Trim();
        if (scorefixPath.Length > 0)
        {
            try
            {
                scorefixPath = Path.GetFullPath(scorefixPath);
            }
            catch (Exception exception)
            {
                await MessageDialog.ShowInfoAsync(this, "Degausser Settings", $"That scorefix folder cannot be used.\n\n{exception.Message}");
                return;
            }
            if (!Directory.Exists(scorefixPath))
            {
                await MessageDialog.ShowInfoAsync(this, "Degausser Settings", "That scorefix folder does not exist. Clear the box to use no scorefixes.");
                return;
            }
        }

        string soundFontPath = (txtSoundFontPath.Text ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(soundFontPath))
        {
            try
            {
                soundFontPath = Path.GetFullPath(soundFontPath);
            }
            catch (Exception exception)
            {
                await MessageDialog.ShowInfoAsync(this, "Degausser Settings", $"That SoundFont path cannot be used.\n\n{exception.Message}");
                return;
            }

            if (!File.Exists(soundFontPath))
            {
                await MessageDialog.ShowInfoAsync(this, "Degausser Settings", "That SoundFont file does not exist.");
                return;
            }
        }

        // Everything is validated above this line; only then is the live settings object touched.
        string profilePath = (txtMidiProfilePath.Text ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(profilePath))
        {
            try
            {
                profilePath = Path.GetFullPath(profilePath);
                BbpMidiProfile.FromFile(profilePath); // validate before persisting
            }
            catch (Exception error)
            {
                await MessageDialog.ShowInfoAsync(this, "Invalid MIDI profile", error.Message);
                return;
            }
        }
        UserSettings settings = UserSettingsStore.Current;
        LibraryPathChanged = !string.Equals(settings.LibraryPath, path, StringComparison.OrdinalIgnoreCase);
        settings.LibraryPath = path;
        ScorefixPathChanged = !string.Equals(settings.ScorefixPath, scorefixPath, StringComparison.OrdinalIgnoreCase);
        settings.ScorefixPath = scorefixPath;
        settings.SoundFontPath = soundFontPath;
        settings.SoundFontMapping = cmbSoundFontMapping.SelectedIndex == 1
            ? SoundFontMappingMode.GeneralMidi
            : SoundFontMappingMode.BbpFaithful;
        settings.BdxDefaultAuthor = (txtBdxAuthor.Text ?? string.Empty).Trim() is { Length: > 0 } author
            ? author : BdxToBbpConverter.DefaultAuthor;
        settings.RememberWindowPlacement = chkRememberWindow.IsChecked == true;
        settings.MidiProfilePath = profilePath;
        settings.MidiTextEncoding = cmbMidiTextEncoding.SelectedIndex == 1 ? "shift-jis" : "utf-8";
        settings.StartupView = cmbStartupView.SelectedIndex switch
        {
            1 => StartupViewPreference.NowPlaying,
            2 => StartupViewPreference.ThreeDsContent,
            _ => StartupViewPreference.LastUsed
        };
        UserSettingsStore.SetRememberSettings(chkRememberSettings.IsChecked == true);
        Close(true);
    }

    private void CancelSettings(object? sender, RoutedEventArgs e) => Close(false);
    private async void BrowseMidiProfile(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile>? files = await PickerGate.RunAsync(async () => await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a BBP2MIDI v1/v2 instrument mapping JSON",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Instrument profile JSON") { Patterns = ["*.json"] }]
        }));
        if (files is { Count: > 0 } && files[0].Path.IsFile)
            txtMidiProfilePath.Text = files[0].Path.LocalPath;
    }
}
