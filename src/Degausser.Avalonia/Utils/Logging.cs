using System.IO;
using System.Diagnostics;
using Serilog;

namespace Degausser;

/// <summary>
/// Desktop-facing logging adapter. Serilog owns persistence/debug output; the
/// event is the application's tiny UI sink for the existing on-screen log.
/// </summary>
internal static class Logging
{
    public static event EventHandler<string>? OnMessage;

    public static string LogDirectory { get; } = Path.Combine(UserSettingsStore.SettingsDirectory, "logs");

    public static void Initialize()
    {
        LoggerConfiguration configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

        // Log files are only kept for users who chose to remember settings; everyone else has the Activity tab.
        if (UserSettingsStore.RememberSettings)
            configuration = configuration.WriteTo.File(
                Path.Combine(LogDirectory, "degausser-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 5_000_000,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}");

        Serilog.Log.Logger = configuration.CreateLogger();
    }

    public static void CloseAndFlush() => Serilog.Log.CloseAndFlush();

    public static void Log(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Serilog.Log.Error(exception, "{ErrorMessage}", exception.Message);
        Publish(exception.Message);
    }

    public static void Log(object? value) => Log(value?.ToString() ?? "(null)");

    public static void Log(string message)
    {
        Serilog.Log.Information("{Message}", message);
        Publish(message);
    }

    public static void AssertEqual<T>(T item1, T item2, string message)
    {
        bool isEqual = EqualityComparer<T>.Default.Equals(item1, item2);
        Debug.Assert(isEqual, message);
        if (!isEqual)
        {
            Serilog.Log.Error("Assertion failed: {AssertionMessage}", message);
            throw new InvalidOperationException(message);
        }
    }

    public static IEnumerable<T> SkipExceptions<T>(this IEnumerable<T> source, bool log)
    {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        bool next = true;
        while (next)
        {
            try
            {
                next = enumerator.MoveNext();
            }
            catch (Exception exception)
            {
                if (log) Log(exception);
                continue;
            }

            if (next) yield return enumerator.Current;
        }
    }

    private static void Publish(string message)
    {
        string uiMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
        OnMessage?.Invoke(null, uiMessage);
    }
}
