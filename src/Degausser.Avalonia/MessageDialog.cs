using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Degausser;

internal enum DialogChoice
{
    Cancel,
    No,
    Yes,
    Ok
}

internal sealed class MessageDialog : Window
{
    private MessageDialog(string title, string message, bool confirmation, string confirmLabel = "OK")
    {
        Title = title;
        Width = 500;
        MinWidth = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid
        {
            Margin = new Thickness(22),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            MaxWidth = 620
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 22, 0, 0)
        };

        if (confirmation)
        {
            var no = new Button { Content = "Cancel", MinWidth = 88 };
            no.Click += (_, _) => Close(DialogChoice.No);
            var yes = new Button { Content = confirmLabel, MinWidth = 88, Classes = { "primary" } };
            yes.Click += (_, _) => Close(DialogChoice.Yes);
            buttons.Children.Add(no);
            buttons.Children.Add(yes);
        }
        else
        {
            var ok = new Button { Content = "OK", MinWidth = 88, Classes = { "primary" } };
            ok.Click += (_, _) => Close(DialogChoice.Ok);
            buttons.Children.Add(ok);
        }

        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(confirmation ? DialogChoice.No : DialogChoice.Ok);
                e.Handled = true;
            }
        };
    }

    public static Task<DialogChoice> ShowInfoAsync(Window owner, string title, string message)
        => new MessageDialog(title, message, confirmation: false).ShowDialog<DialogChoice>(owner);

    public static Task<DialogChoice> ShowConfirmationAsync(Window owner, string title, string message, string confirmLabel = "OK")
        => new MessageDialog(title, message, confirmation: true, confirmLabel).ShowDialog<DialogChoice>(owner);
}
