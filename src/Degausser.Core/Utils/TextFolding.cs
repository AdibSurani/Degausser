using System.Text;

namespace Degausser;

/// <summary>
/// Unicode compatibility folding (NFKC) that also works in the browser. .NET's
/// WebAssembly runtime can't do NFKC: in invariant-globalization mode Normalize
/// leaves non-ASCII text unchanged, and its ICU build throws. Where the runtime
/// can't, a table covers what lyrics and catalogue text contain: full-width ASCII
/// and spaces, half-width katakana, Roman numerals, ellipses, circled numbers and,
/// for search, accented Latin letters.
/// </summary>
internal static class TextFolding
{
    /// <summary>False where the runtime can't normalise. DEGAUSSER_TEXT_FOLDING=table forces the table, for testing.</summary>
    internal static bool UseRuntime { get; } =
        Environment.GetEnvironmentVariable("DEGAUSSER_TEXT_FOLDING") != "table" && RuntimeNormalises();

    // U+FF61..U+FF9F.
    private const string HalfwidthKatakana = "。「」、・ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワン\u3099\u309A";

    // Pairs of kana and the same kana with a (semi-)voiced mark, as NFC composes them.
    private const string Voiced = "うゔかがきぎくぐけげこごさざしじすずせぜそぞただちぢつづてでとどはばひびふぶへべほぼゝゞウヴカガキギクグケゲコゴサザシジスズセゼソゾタダチヂツヅテデトドハバヒビフブヘベホボワヷヰヸヱヹヲヺヽヾ";
    private const string SemiVoiced = "はぱひぴふぷへぺほぽハパヒピフプヘペホポ";

    // Precomposed Latin-1 and Latin Extended-A letters, and their base letters.
    private const string Accented = "ÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝàáâãäåçèéêëìíîïñòóôõöùúûüýÿĀāĂăĄąĆćĈĉĊċČčĎďĒēĔĕĖėĘęĚěĜĝĞğĠġĢģĤĥĨĩĪīĬĭĮįİĴĵĶķĹĺĻļĽľŃńŅņŇňŌōŎŏŐőŔŕŖŗŘřŚśŜŝŞşŠšŢţŤťŨũŪūŬŭŮůŰűŲųŴŵŶŷŸŹźŻżŽž";
    private const string Unaccented = "AAAAAACEEEEIIIINOOOOOUUUUYaaaaaaceeeeiiiinooooouuuuyyAaAaAaCcCcCcCcDdEeEeEeEeEeGgGgGgGgHhIiIiIiIiIJjKkLlLlLlNnNnNnOoOoOoRrRrRrSsSsSsSsTtTtUuUuUuUuUuUuWwYyYZzZzZz";

    private static readonly string[] RomanNumerals =
        ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "L", "C", "D", "M",
         "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x", "xi", "xii", "l", "c", "d", "m"];

    private static readonly Dictionary<char, string> Others = new()
    {
        ['\u00A0'] = " ", ['\u2002'] = " ", ['\u2003'] = " ", ['\u2009'] = " ", ['\u202F'] = " ", ['\u3000'] = " ",
        ['\u2024'] = ".", ['\u2025'] = "..", ['\u2026'] = "...",
        ['\u203C'] = "!!", ['\u2047'] = "??", ['\u2048'] = "?!", ['\u2049'] = "!?", ['\u2033'] = "′′",
        ['\u00B9'] = "1", ['\u00B2'] = "2", ['\u00B3'] = "3", ['\u00AA'] = "a", ['\u00BA'] = "o",
        ['\u00BC'] = "1⁄4", ['\u00BD'] = "1⁄2", ['\u00BE'] = "3⁄4",
        ['\u2122'] = "TM", ['\u2116'] = "No", ['\u2103'] = "°C",
        ['\uFFE0'] = "¢", ['\uFFE1'] = "£", ['\uFFE2'] = "¬", ['\uFFE4'] = "¦", ['\uFFE5'] = "¥",
    };

    /// <summary>NFKC: the runtime's where it can, otherwise <see cref="CompatibilityTable"/>.</summary>
    public static string Compatibility(string text)
        => UseRuntime ? text.Normalize(NormalizationForm.FormKC) : CompatibilityTable(text);

    internal static string CompatibilityTable(string text)
    {
        if (Ascii.IsValid(text)) return text;
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is >= '！' and <= '～') builder.Append((char)(c - 0xFEE0));
            else if (c is >= '｡' and <= 'ﾟ') AppendComposed(builder, HalfwidthKatakana[c - 0xFF61]);
            else if (c is >= 'Ⅰ' and <= 'ⅿ') builder.Append(RomanNumerals[c - 0x2160]);
            else if (c is >= '①' and <= '⑳') builder.Append(c - 0x245F);
            else if (Others.TryGetValue(c, out string? replacement)) builder.Append(replacement);
            else AppendComposed(builder, c);
        }
        return builder.ToString();
    }

    /// <summary>Precomposed Latin letters without their diacritics (é → e, ō → o).</summary>
    internal static string StripLatinAccents(string text)
    {
        if (Ascii.IsValid(text)) return text;
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            int index = Accented.IndexOf(chars[i]);
            if (index >= 0) chars[i] = Unaccented[index];
        }
        return new string(chars);
    }

    private static void AppendComposed(StringBuilder builder, char c)
    {
        if (builder.Length > 0 && c is '\u3099' or '\u309A')
        {
            string pairs = c == '\u3099' ? Voiced : SemiVoiced;
            for (int i = 0; i < pairs.Length; i += 2)
            {
                if (pairs[i] != builder[^1]) continue;
                builder[^1] = pairs[i + 1];
                return;
            }
        }
        builder.Append(c);
    }

    private static bool RuntimeNormalises()
    {
        try
        {
            return "\uFF21\uFF76\uFF9E".Normalize(NormalizationForm.FormKC) == "A\u30AC";
        }
        catch (Exception)
        {
            return false; // the browser's ICU build throws for NFKC
        }
    }
}
