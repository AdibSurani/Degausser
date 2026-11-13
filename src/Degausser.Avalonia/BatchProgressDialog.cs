using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Degausser;

/// <summary>UI-only snapshot of a sequential, potentially save-mutating batch.</summary>
internal readonly record struct BatchProgress(int Completed, int Total, string ItemName, int Failed);

/// <summary>
/// Modal progress window. The owner is disabled while the batch changes its
/// workspace, but Avalonia's dispatcher remains responsive because all file
/// work runs on a background thread. A cancellable batch stops between items,
/// never inside one: the item in progress finishes, and the batch's closing
/// steps (saving the song manager, reloading) still run, so what was already
/// imported or extracted is kept and nothing is left half-written.
/// </summary>
internal sealed class BatchProgressDialog : Window
{
    private readonly string verb;
    private readonly TextBlock headline;
    private readonly TextBlock filename;
    private readonly TextBlock failures;
    private readonly ProgressBar progressBar;
    private readonly TextBlock safetyNote;
    private readonly Button? cancelButton;
    private readonly CancellationTokenSource cancellation = new();
    private bool mayClose;

    /// <summary>Signalled when the user asks to stop; the batch checks it between items.</summary>
    internal CancellationToken CancellationToken => cancellation.Token;

    internal BatchProgressDialog(string verb, int total, bool cancellable = false)
    {
        this.verb = verb;
        Title = $"{verb}…";
        Width = 490;
        MinWidth = 390;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        headline = new TextBlock
        {
            Text = Headline(0, total),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        };
        filename = new TextBlock
        {
            Text = "Preparing…",
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 440
        };
        progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, total),
            Value = 0,
            Height = 8,
            IsIndeterminate = false
        };
        failures = new TextBlock { Text = "", FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        safetyNote = new TextBlock
        {
            Text = verb switch
            {
                "Importing" => "Updating your 3DS content. A restore point was made first; keep Degausser open until this finishes.",
                "Extracting" => "Writing files into your library folder.",
                _ => "Keep Degausser open until this finishes."
            },
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        };
        var panel = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 12,
            Children = { headline, filename, progressBar, failures, safetyNote }
        };
        if (cancellable)
        {
            cancelButton = new Button { Content = "Cancel", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
            cancelButton.Click += (_, _) => RequestCancel();
            panel.Children.Add(cancelButton);
        }
        Content = panel;

        // The window can't close mid-batch; on a cancellable batch, closing it asks to stop instead.
        Closing += (_, args) =>
        {
            if (mayClose) return;
            args.Cancel = true;
            if (cancellable) RequestCancel();
        };
    }

    private void RequestCancel()
    {
        if (mayClose || cancellation.IsCancellationRequested) return;
        cancellation.Cancel();
        if (cancelButton is not null)
        {
            cancelButton.Content = "Cancelling…";
            cancelButton.IsEnabled = false;
        }
        safetyNote.Text = verb == "Importing"
            ? "Stopping after the current item. Everything imported so far is kept and saved."
            : "Stopping after the current item. Everything done so far is kept.";
    }

    internal void Update(BatchProgress update)
    {
        if (mayClose) return;
        int total = Math.Max(0, update.Total);
        int complete = Math.Clamp(update.Completed, 0, total);
        headline.Text = Headline(complete, total);
        filename.Text = update.ItemName;
        progressBar.Maximum = Math.Max(1, total);
        progressBar.Value = complete;
        failures.Text = update.Failed == 0 ? "" : $"{update.Failed:N0} failed; details in Activity";
    }

    // A single-step job ("Translating", "Restoring") reads better without "0 of 1".
    private string Headline(int complete, int total)
        => total <= 1 ? $"{verb}…" : $"{verb} {complete:N0} of {total:N0}…";

    internal void Finish()
    {
        mayClose = true;
        Close();
        cancellation.Dispose();
    }
}
