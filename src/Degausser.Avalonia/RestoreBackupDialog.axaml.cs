using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Degausser;

/// <summary>Lists a save's restore points and returns the one the user picks (or null).</summary>
public partial class RestoreBackupDialog : Window
{
    private readonly string backupDirectory = string.Empty;
    private readonly List<ExtDataBackup.BackupInfo> backups = [];

    public RestoreBackupDialog() => InitializeComponent();

    internal RestoreBackupDialog(string saveName, string backupDirectory) : this()
    {
        this.backupDirectory = backupDirectory;
        txtIntro.Text = $"Degausser saves a restore point of {saveName} before every change. Restoring one puts the save's song, movie and "
            + "singer lists back as they were then, along with any songs deleted since. Songs added afterwards stay on the card but are no longer listed. "
            + "The current state is kept as a new restore point first, so a restore can be undone.";
        backups.AddRange(ExtDataBackup.List(backupDirectory));
        lstBackups.ItemsSource = backups.Select(Describe).ToList();
        txtEmpty.IsVisible = backups.Count == 0;
        if (backups.Count > 0) lstBackups.SelectedIndex = 0;
    }

    private static string Describe(ExtDataBackup.BackupInfo info)
    {
        string kept = info.PayloadFiles == 0 ? string.Empty : $"  ·  includes {info.PayloadFiles:N0} song file(s) changed or removed after it";
        return $"{info.Created:d MMM yyyy, HH:mm:ss}{kept}";
    }

    private void SelectionChanged(object? sender, SelectionChangedEventArgs e)
        => btnRestore.IsEnabled = lstBackups.SelectedIndex >= 0;

    private void RestoreClicked(object? sender, RoutedEventArgs e)
    {
        if (lstBackups.SelectedIndex is >= 0 and var index && index < backups.Count)
            Close(backups[index].Path);
    }

    private async void OpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(backupDirectory);
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(backupDirectory));
        }
        catch (Exception exception)
        {
            Logging.Log(exception);
        }
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
