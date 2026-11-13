namespace Degausser;

/// <summary>
/// UI/session state for one opened extdata workspace.
/// </summary>
internal sealed class ExtDataSession
{
    public ExtDataWorkspace Workspace { get; }
    public List<BBPRecord> Songs { get; private set; } = [];
    public List<ExtDataWorkspace.MovieEntry> Movies { get; private set; } = [];
    public List<ExtDataWorkspace.SingerEntry> Singers { get; private set; } = [];
    public string SearchText { get; set; } = string.Empty;

    public string DisplayName => Workspace.DisplayName;
    public string EntryFilePath => Workspace.EntryFilePath;
    /// <summary>The save data folder the user opened (the one holding jb, mv and singer).</summary>
    public string FolderPath => Workspace.ExtDataRootDirectory;
    public string RootDirectory => Workspace.RootDirectory;

    public ExtDataSession(string mgrPath)
    {
        Workspace = new ExtDataWorkspace(mgrPath);
        Workspace.BackupDirectory = BackupDirectoryFor(Workspace.ExtDataRootDirectory);
    }

    /// <summary>Per-save folder for restore points, kept in the app's data folder rather than on the SD card.</summary>
    public static string BackupDirectoryFor(string extDataRoot)
    {
        string full = Path.GetFullPath(extDataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = new string((Path.GetFileName(full)).Where(char.IsLetterOrDigit).ToArray());
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(full)))[..8];
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Degausser", "backups", $"{(name.Length == 0 ? "save" : name)}-{hash}");
    }

    public string BackupDirectory => Workspace.BackupDirectory ?? BackupDirectoryFor(Workspace.ExtDataRootDirectory);

    public void ReloadAll(Action<string>? warning = null)
    {
        Songs = Workspace.ReadSongs(warning);
        Movies = Workspace.ReadMovies(warning);
        Singers = Workspace.ReadSingers(warning);
    }

    public void ReloadSongs(Action<string>? warning = null) => Songs = Workspace.ReadSongs(warning);

    public override string ToString() => DisplayName;
}
