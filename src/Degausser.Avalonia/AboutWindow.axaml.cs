using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Degausser;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        txtVersion.Text = $"Version {AppVersion.Full}";
    }

    private void OpenGitHub(object? sender, RoutedEventArgs e) => OpenUrl("https://github.com/AdibSurani/Degausser");
    private void OpenLicenses(object? sender, RoutedEventArgs e) => OpenUrl("https://github.com/AdibSurani/Degausser/blob/main/THIRD-PARTY-NOTICES.md");
    private void CloseAbout(object? sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception exception) { Logging.Log(exception); }
    }
}
