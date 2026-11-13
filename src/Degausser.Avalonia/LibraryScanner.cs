using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Degausser;

internal readonly record struct LibraryScanProgress(int Completed, int Total, int Loaded, int Failed)
{
    public bool IsEnumerating => Total < 0;
}

internal sealed record LibraryScanResult(
    List<LibrarySong> Songs,
    int Failed,
    TimeSpan Elapsed);

/// <summary>
/// Cancellable library discovery. The expensive part of opening a song is
/// intentionally deferred to LibrarySong.LoadRecord().
/// </summary>
internal static class LibraryScanner
{
    public static Task<LibraryScanResult> ScanAsync(
        string libraryPath,
        IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken)
        => Task.Run(() => Scan(libraryPath, progress, cancellationToken), cancellationToken);

    private static LibraryScanResult Scan(
        string libraryPath,
        IProgress<LibraryScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        // The default library folder is not created until the user puts something in it.
        if (!Directory.Exists(libraryPath))
            return new LibraryScanResult([], 0, stopwatch.Elapsed);
        progress?.Report(new LibraryScanProgress(0, -1, 0, 0));

        var paths = new List<string>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (string path in Directory.EnumerateFiles(libraryPath, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(path);
            if (extension.Equals(".bbp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bdx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bbpm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bbpv", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        paths.Sort((a, b) =>
        {
            int byName = StringComparer.CurrentCultureIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b));
            return byName != 0 ? byName : StringComparer.OrdinalIgnoreCase.Compare(a, b);
        });

        int total = paths.Count;
        progress?.Report(new LibraryScanProgress(0, total, 0, 0));
        if (total == 0)
            return new LibraryScanResult([], 0, stopwatch.Elapsed);

        var results = new LibrarySong?[total];
        var failures = new ConcurrentQueue<(string Path, Exception Exception)>();
        int completed = 0;
        int loaded = 0;
        int failed = 0;

        int parallelism = Math.Clamp(Environment.ProcessorCount, 2, 8);
        Parallel.For(
            0,
            total,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelism
            },
            i =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results[i] = LibrarySong.Read(paths[i]);
                    Interlocked.Increment(ref loaded);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref failed);
                    failures.Enqueue((paths[i], exception));
                }
                finally
                {
                    int done = Interlocked.Increment(ref completed);
                    // Avoid posting thousands of dispatcher callbacks while
                    // still keeping the UI visibly alive.
                    if (done == total || done % 32 == 0)
                    {
                        progress?.Report(new LibraryScanProgress(
                            done,
                            total,
                            Volatile.Read(ref loaded),
                            Volatile.Read(ref failed)));
                    }
                }
            });

        (string Path, Exception Exception)[] failureList = failures.ToArray();
        foreach ((string path, Exception exception) in failureList.Take(20))
            Logging.Log($"Could not read library item {path}: {exception.Message}");
        if (failureList.Length > 20)
            Logging.Log($"Library scan skipped {failureList.Length - 20:N0} additional unreadable file(s); see the count above rather than flooding the activity log.");

        var songs = results.Where(song => song is not null).Select(song => song!).ToList();
        stopwatch.Stop();
        return new LibraryScanResult(songs, failed, stopwatch.Elapsed);
    }
}
