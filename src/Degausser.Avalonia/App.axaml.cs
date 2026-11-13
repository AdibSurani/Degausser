using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Degausser;

public partial class App : Application
{
    public override void Initialize()
        => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        UserSettingsStore.Initialize();
        Logging.Initialize();
        UserSettingsStore.LoadScorefixes();
        InstallExceptionHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.Exit += (_, _) =>
            {
                MidiPlayer.Instance.Dispose();
                Logging.CloseAndFlush();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // An exception thrown by an async void handler or a UI callback would otherwise
    // end the process. Report it, keep the window, and leave the details in the log.
    private static void InstallExceptionHandlers()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Logging.Log($"Unhandled UI exception: {e.Exception}");
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
                _ = MessageDialog.ShowInfoAsync(owner, "Something went wrong",
                    $"{e.Exception.Message}\n\n" + (UserSettingsStore.RememberSettings
                        ? $"Details were written to the log in {Logging.LogDirectory}."
                        : "Details are in the Activity tab."));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Logging.Log($"Unobserved task exception: {e.Exception}");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logging.Log($"Fatal exception: {e.ExceptionObject}");
    }
}
