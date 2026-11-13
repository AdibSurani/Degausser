using System.Globalization;

namespace Degausser;

/// <summary>
/// Restore points for a save's manager files. A snapshot holds a copy of every file
/// directly inside jb/, mv/ and singer/ (the managers and their mirrors, about 6 MB;
/// the large gak/data payloads are only ever added, never overwritten) under
/// <c>managers/</c>, plus any payload file or folder that an operation removed or
/// rewrote under <c>payloads/</c>, so restoring never leaves a manager pointing at
/// files that are gone.
/// </summary>
public static class ExtDataBackup
{
    public const int MaxSnapshots = 30;

    private const string ManagersFolder = "managers";
    private const string PayloadsFolder = "payloads";
    private static readonly string[] ManagerDirectories = ["jb", "mv", "singer"];

    public sealed record BackupInfo(string Path, DateTime Created, int ManagerFiles, int PayloadFiles)
    {
        public string Name => System.IO.Path.GetFileName(Path);
    }

    /// <summary>
    /// Copies the save's manager files into a new snapshot under <paramref name="backupRoot"/>
    /// and returns its path. If the newest snapshot already holds identical files, that one is
    /// returned instead of making a duplicate.
    /// </summary>
    public static string Create(string extDataRoot, string backupRoot)
    {
        List<(string Relative, string Full)> files = ManagerFiles(extDataRoot);
        if (files.Count == 0)
            throw new DirectoryNotFoundException($"No manager files were found under '{extDataRoot}'.");

        Directory.CreateDirectory(backupRoot);
        string? newest = Snapshots(backupRoot).FirstOrDefault();
        if (newest is not null && MatchesSnapshot(newest, files))
            return newest;

        string name = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = System.IO.Path.Combine(backupRoot, name);
        for (int i = 2; Directory.Exists(path); i++)
            path = System.IO.Path.Combine(backupRoot, $"{name}-{i}");

        // Built under a temporary name so an interrupted copy never looks like a usable snapshot.
        string building = path + ".partial";
        try
        {
            foreach ((string relative, string full) in files)
            {
                string destination = System.IO.Path.Combine(building, ManagersFolder, relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                File.Copy(full, destination);
            }
            Directory.Move(building, path);
        }
        catch
        {
            try { Directory.Delete(building, true); } catch { }
            throw;
        }

        foreach (string old in Snapshots(backupRoot).Skip(MaxSnapshots))
        {
            try { Directory.Delete(old, true); } catch { }
        }
        return path;
    }

    /// <summary>Snapshots under <paramref name="backupRoot"/>, newest first.</summary>
    public static IReadOnlyList<BackupInfo> List(string backupRoot)
        => Snapshots(backupRoot).Select(path => new BackupInfo(
            path,
            CreatedAt(path),
            CountFiles(System.IO.Path.Combine(path, ManagersFolder)),
            CountFiles(System.IO.Path.Combine(path, PayloadsFolder)))).ToList();

    /// <summary>
    /// Keeps a copy of a payload file or folder inside the snapshot before an operation
    /// rewrites it. Returns without copying if the snapshot already has that path.
    /// </summary>
    public static void Preserve(string snapshotPath, string extDataRoot, string path)
    {
        string relative = System.IO.Path.GetRelativePath(extDataRoot, path);
        string destination = System.IO.Path.Combine(snapshotPath, PayloadsFolder, relative);
        if (File.Exists(path))
        {
            if (File.Exists(destination)) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            File.Copy(path, destination);
        }
        else if (Directory.Exists(path))
        {
            if (Directory.Exists(destination)) return;
            CopyDirectory(path, destination);
        }
    }

    /// <summary>
    /// Copies a snapshot's files back over the save. The current state is snapshotted first, so a
    /// restore can itself be undone. Returns the path of that safety snapshot.
    /// </summary>
    public static string Restore(string snapshotPath, string extDataRoot)
    {
        string managers = System.IO.Path.Combine(snapshotPath, ManagersFolder);
        if (!Directory.Exists(managers))
            throw new DirectoryNotFoundException($"'{snapshotPath}' is not a Degausser backup.");

        string safety = Create(extDataRoot, System.IO.Path.GetDirectoryName(snapshotPath)!);

        string payloads = System.IO.Path.Combine(snapshotPath, PayloadsFolder);
        if (Directory.Exists(payloads))
        {
            foreach (string file in Directory.EnumerateFiles(payloads, "*", SearchOption.AllDirectories))
                CopyAtomic(file, System.IO.Path.Combine(extDataRoot, System.IO.Path.GetRelativePath(payloads, file)));
        }

        // Managers last, and the mirror after its primary, as when the app writes them.
        foreach (string file in Directory.EnumerateFiles(managers, "*", SearchOption.AllDirectories)
                     .OrderBy(f => System.IO.Path.GetFileNameWithoutExtension(f).EndsWith('_')))
            CopyAtomic(file, System.IO.Path.Combine(extDataRoot, System.IO.Path.GetRelativePath(managers, file)));
        return safety;
    }

    // The folder name is the time the snapshot was made; it survives copying the folder around.
    private static DateTime CreatedAt(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        return name.Length >= 15 && DateTime.TryParseExact(name[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : Directory.GetCreationTime(path);
    }

    private static List<(string Relative, string Full)> ManagerFiles(string extDataRoot)
    {
        var result = new List<(string, string)>();
        foreach (string directory in ManagerDirectories)
        {
            string full = System.IO.Path.Combine(extDataRoot, directory);
            if (!Directory.Exists(full)) continue;
            foreach (string file in Directory.EnumerateFiles(full).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add((System.IO.Path.Combine(directory, System.IO.Path.GetFileName(file)), file));
            }
        }
        return result;
    }

    private static IEnumerable<string> Snapshots(string backupRoot)
        => Directory.Exists(backupRoot)
            ? Directory.EnumerateDirectories(backupRoot)
                .Where(d => !d.EndsWith(".partial", StringComparison.Ordinal)
                            && Directory.Exists(System.IO.Path.Combine(d, ManagersFolder)))
                .OrderByDescending(d => System.IO.Path.GetFileName(d), StringComparer.Ordinal)
            : [];

    private static bool MatchesSnapshot(string snapshotPath, List<(string Relative, string Full)> files)
    {
        string managers = System.IO.Path.Combine(snapshotPath, ManagersFolder);
        if (CountFiles(managers) != files.Count) return false;
        foreach ((string relative, string full) in files)
        {
            string copy = System.IO.Path.Combine(managers, relative);
            if (!File.Exists(copy) || !FilesEqual(copy, full)) return false;
        }
        return true;
    }

    private static bool FilesEqual(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var x = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var y = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] bx = new byte[81920], by = new byte[81920];
        while (true)
        {
            int nx = x.ReadAtLeast(bx, bx.Length, throwOnEndOfStream: false);
            int ny = y.ReadAtLeast(by, by.Length, throwOnEndOfStream: false);
            if (nx != ny || !bx.AsSpan(0, nx).SequenceEqual(by.AsSpan(0, ny))) return false;
            if (nx == 0) return true;
        }
    }

    private static int CountFiles(string directory)
        => Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count() : 0;

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CopyAtomic(string source, string destination)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
        string temporary = destination + ".tmp";
        try
        {
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
