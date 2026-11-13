using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Degausser;

/// <summary>
/// One visual karaoke line. The current timing range is split into Avalonia
/// Runs, which lets the active run use a hard-edged horizontal gradient as its
/// foreground. Unlike the first Avalonia port, the sweep can therefore stop
/// inside a glyph instead of advancing only in whole-character steps.
/// </summary>
public sealed class KaraokeLineView : Grid
{
    private static readonly Color HighlightColor = Color.Parse("#F04444");
    private static readonly IBrush HighlightBrush = new SolidColorBrush(HighlightColor);
    private static readonly IBrush BaseTextBrush = Brushes.White;
    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.Parse("#0A0C10"));
    // Keep Inter for Latin text, but do not let the platform choose a narrow UI
    // fallback for Japanese glyphs. Meiryo/Yu Gothic are the preferred Windows
    // faces; Hiragino and Noto make the same intent explicit on macOS/Linux.
    private static readonly FontFamily KaraokeFontFamily =
        new("Inter, Meiryo, Yu Gothic, Hiragino Sans, Noto Sans CJK JP");

    private readonly string _text;
    private readonly Run[] _foregroundRuns;
    private readonly LinearGradientBrush[] _sweepBrushes;
    private readonly GradientStop[] _sweepRedStops;
    private readonly GradientStop[] _sweepWhiteStops;

    public KaraokeLineView(string text)
    {
        _text = string.IsNullOrEmpty(text) ? " " : text;
        Margin = new Thickness(28, 4);
        HorizontalAlignment = HorizontalAlignment.Stretch;

        // Recreate the strong game-like lyric outline without a custom text
        // renderer: eight identical TextBlocks sit behind the foreground at
        // small offsets. Because they share the same width/wrapping settings,
        // wrapped lyrics stay aligned as the window changes size.
        foreach ((double x, double y) in OutlineOffsets)
        {
            TextBlock outline = CreateTextBlock(_text, OutlineBrush);
            outline.RenderTransform = new TranslateTransform(x, y);
            outline.IsHitTestVisible = false;
            Children.Add(outline);
        }

        // Keep the foreground Run boundaries stable for the lifetime of the line.
        // Re-splitting one TextBlock into completed/active/upcoming Runs on every
        // karaoke tick changes shaping/kerning at the boundaries, which makes the
        // active row visibly grow/shrink by a pixel or two. One fixed Run per UTF-16
        // code unit avoids that layout jitter; only brushes change while singing.
        _foregroundRuns = new Run[_text.Length];
        _sweepBrushes = new LinearGradientBrush[_text.Length];
        _sweepRedStops = new GradientStop[_text.Length];
        _sweepWhiteStops = new GradientStop[_text.Length];

        TextBlock foreground = CreateTextBlock(string.Empty, BaseTextBrush);
        for (int i = 0; i < _text.Length; i++)
        {
            var red = new GradientStop(HighlightColor, 0);
            var white = new GradientStop(Colors.White, 0);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(HighlightColor, 0),
                    red,
                    white,
                    new GradientStop(Colors.White, 1)
                }
            };
            var run = new Run { Text = _text[i].ToString(), Foreground = BaseTextBrush };
            _foregroundRuns[i] = run;
            _sweepBrushes[i] = brush;
            _sweepRedStops[i] = red;
            _sweepWhiteStops[i] = white;
            foreground.Inlines!.Add(run);
        }
        foreground.IsHitTestVisible = false;
        Children.Add(foreground);

        SetUpcoming();
    }

    public int CharacterCount => _text.Length;

    public void SetCompleted()
    {
        // Match the pre-Avalonia player: old lines are dim white rather than remaining
        // red after the sung line has moved on.
        Opacity = 0.42;
        SetPlainText();
    }

    public void SetUpcoming()
    {
        Opacity = 0.68;
        SetPlainText();
    }

    public void SetCurrent(int completedCharacters, int activeStart, int activeCount, double fraction)
    {
        Opacity = 1;
        int length = CharacterCount;
        int completed = Math.Clamp(completedCharacters, 0, length);
        int start = Math.Clamp(activeStart, completed, length);
        int count = Math.Clamp(activeCount, 0, length - start);
        int end = start + count;

        double activeProgress = Math.Clamp(fraction, 0, 1) * count;
        for (int i = 0; i < _foregroundRuns.Length; i++)
        {
            if (i < completed)
            {
                _foregroundRuns[i].Foreground = HighlightBrush;
                continue;
            }
            if (i < start || i >= end)
            {
                _foregroundRuns[i].Foreground = BaseTextBrush;
                continue;
            }

            // Convert the whole active-range progress into a local 0..1 sweep for
            // this glyph. Duplicate stops make the boundary hard-edged.
            double local = Math.Clamp(activeProgress - (i - start), 0, 1);
            if (local <= 0)
                _foregroundRuns[i].Foreground = BaseTextBrush;
            else if (local >= 1)
                _foregroundRuns[i].Foreground = HighlightBrush;
            else
            {
                _sweepRedStops[i].Offset = local;
                _sweepWhiteStops[i].Offset = local;
                _foregroundRuns[i].Foreground = _sweepBrushes[i];
            }
        }
    }

    private void SetPlainText()
    {
        foreach (Run run in _foregroundRuns)
            run.Foreground = BaseTextBrush;
    }

    private static TextBlock CreateTextBlock(string text, IBrush foreground) => new()
    {
        Text = text,
        FontFamily = KaraokeFontFamily,
        FontSize = 24,
        FontWeight = FontWeight.SemiBold,
        TextAlignment = TextAlignment.Left,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 38,
        Foreground = foreground,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static readonly (double X, double Y)[] OutlineOffsets =
    [
        (-1.2, 0), (1.2, 0), (0, -1.2), (0, 1.2),
        (-0.9, -0.9), (0.9, -0.9), (-0.9, 0.9), (0.9, 0.9)
    ];
}
