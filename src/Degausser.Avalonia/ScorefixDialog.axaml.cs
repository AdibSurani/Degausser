using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Degausser;

public partial class ScorefixDialog : Window
{
    public ScorefixDialog() : this(null, null) { }

    /// <param name="bundled">Degausser's own scorefix for this song, offered as the starting text.</param>
    /// <param name="template">Otherwise, the song's current title and lyrics as a skeleton to edit.</param>
    public ScorefixDialog(string? bundled, string? template)
    {
        InitializeComponent();
        if (bundled is null && template is not null)
        {
            txtScorefix.Text = template;
            txtIntro.Text = "Below are this song's current title and lyrics (usually Japanese). Replace the lyrics with romaji, one line for each line, "
                + "and change the title if you like; the ID line and the separators must stay. The romaji is checked against the song's Japanese karaoke before anything changes. "
                + "To change only the title, delete everything below the second separator line; the lyrics are then left as they are. "
                + "Brackets around one Japanese character are alignment overrides and aren't shown.";
        }
        if (bundled is not null)
        {
            txtScorefix.Text = bundled;
            txtIntro.Text = "Degausser has English lyrics for this song, shown below. Apply them as they are, or edit or paste your own scorefix. "
                + "The romaji is checked against the song's Japanese karaoke before anything changes. To change only the title, delete everything below the second separator line. "
                + "Brackets around one Japanese character are alignment overrides and aren't shown.";
        }
        Opened += (_, _) => txtScorefix.Focus();
    }

    private void ApplyClicked(object? sender, RoutedEventArgs e)
        => Close(txtScorefix.Text ?? string.Empty);

    private void CancelClicked(object? sender, RoutedEventArgs e)
        => Close(null);
}
