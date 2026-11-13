using System.Globalization;

namespace Degausser;

/// <summary>
/// Historical Degausser scorefix text: optional 8-hex-digit title ID,
/// separator, display title, separator, then replacement romaji lyrics.
/// Built-in ROMFS scorefixes omit the title-ID header and therefore have only
/// the separator between title and lyrics. A scorefix for a song with no fixed ID (a BDX) starts
/// with <c>lyrics:</c> and a 16-digit fingerprint of the Japanese karaoke instead of the ID
/// (see <see cref="Scorefix.Fingerprint(string)"/>).
/// </summary>
public sealed record ScorefixDocument(uint? TitleId, string Title, string Lyrics)
{
    /// <summary>The header line's fingerprint of the Japanese lyrics this scorefix is for, or null.</summary>
    public string? Fingerprint { get; init; }

    public string FlatTitle => Title.Replace("\r", string.Empty).Replace("\n", string.Empty);

    public static ScorefixDocument Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        int index = 0;
        uint? titleId = null;
        string? fingerprint = null;

        if (lines.Length > 1 && lines[0].Length == 8 && IsSeparator(lines[1])
            && uint.TryParse(lines[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsedId))
        {
            titleId = parsedId;
            index = 2;
        }
        else if (lines.Length > 1 && IsSeparator(lines[1]) && TryParseFingerprintLine(lines[0], out string? parsedFingerprint))
        {
            fingerprint = parsedFingerprint;
            index = 2;
        }

        int titleStart = index;
        while (index < lines.Length && !IsSeparator(lines[index])) index++;
        if (index >= lines.Length)
            throw new FormatException("Scorefix is missing the separator between title and lyrics.");
        if (index == titleStart)
            throw new FormatException("Scorefix title is empty.");

        string title = string.Join('\n', lines[titleStart..index]);
        index++; // separator
        string lyrics = string.Join('\n', lines[index..]).TrimEnd('\n');

        if (title.Length > 249)
            throw new FormatException($"Scorefix title is too long for GAK ({title.Length} characters; maximum 249).");
        // The manager keeps the line breaks (see Apply), so they count towards its 50 characters.
        if (title.Length > 50)
            throw new FormatException($"Scorefix title is too long for JbMgr ({title.Length} characters with its line breaks; maximum 50).");
        if (lyrics.Length > 2999)
            throw new FormatException($"Scorefix lyrics are too long ({lyrics.Length} characters; maximum 2999).");

        return new ScorefixDocument(titleId, title, lyrics) { Fingerprint = fingerprint };
    }

    private static bool TryParseFingerprintLine(string line, out string? fingerprint)
    {
        fingerprint = null;
        string trimmed = line.Trim();
        if (!trimmed.StartsWith(FingerprintPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        string value = trimmed[FingerprintPrefix.Length..].Trim().ToUpperInvariant();
        if (value.Length != Scorefix.FingerprintLength || !value.All(Uri.IsHexDigit)) return false;
        fingerprint = value;
        return true;
    }

    public const string FingerprintPrefix = "lyrics:";

    private static bool IsSeparator(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        string trimmed = line.Trim();
        return trimmed.Length >= 3 && trimmed.All(c => c is '―' or '—' or '-' or '─');
    }
}

public static class Scorefix
{
    /// <summary>Hex digits in a fingerprint (64 bits of SHA-256).</summary>
    public const int FingerprintLength = 16;

    /// <summary>
    /// Identifies a song by its Japanese karaoke lyrics, for songs whose title ID can't (a BDX's is a
    /// 16-bit checksum that can collide, and changes on import). The lyrics are NFKC-normalised and
    /// stripped of whitespace and NULs, so line breaks and width variants don't matter; the result is
    /// the first 8 bytes of their SHA-256 as 16 upper-case hex digits. Null when there are no lyrics
    /// (an instrumental has nothing to fingerprint).
    /// </summary>
    public static string? Fingerprint(string? karaokeLyrics)
    {
        if (string.IsNullOrEmpty(karaokeLyrics)) return null;
        var text = new System.Text.StringBuilder(karaokeLyrics.Length);
        foreach (char c in karaokeLyrics.Normalize(System.Text.NormalizationForm.FormKC))
            if (c != '\0' && !char.IsWhiteSpace(c)) text.Append(c);
        if (text.Length == 0) return null;
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        return System.Convert.ToHexString(hash, 0, FingerprintLength / 2);
    }

    /// <summary>The fingerprint of the song's Japanese karaoke, or null if it has none.</summary>
    public static string? Fingerprint(BBPRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.gak.HasKaraoke ? Fingerprint(record.gak.karaokeLyrics) : null;
    }

    /// <summary>A BDX conversion's temporary ID range (0x8000xxxx); the ID says nothing about the song.</summary>
    public static bool IsCustomId(uint titleId) => titleId != 0xFFFFFFFF && titleId >> 16 == 0x8000;

    /// <summary>
    /// A scorefix skeleton for <paramref name="record"/>: its title ID (or, for a custom song whose ID
    /// changes on import, a fingerprint of its lyrics), its current title and its current karaoke lyrics, line by line. Replace the
    /// lyrics with romaji and keep the title if it is already fine. Applying it unedited fails, because the
    /// aligner expects romaji, not the Japanese it came from.
    /// </summary>
    public static string Template(BBPRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        const string separator = "――――――――――――";
        uint id = record.mgrItem.titleID;
        var lines = new List<string>();
        if (id != 0xFFFFFFFF && !IsCustomId(id))
        {
            lines.Add(id.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
            lines.Add(separator);
        }
        else if (Fingerprint(record) is string fingerprint)
        {
            lines.Add(ScorefixDocument.FingerprintPrefix + fingerprint);
            lines.Add(separator);
        }
        string title = (record.gak.title ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
        lines.Add(title.Length > 0 ? title : record.mgrItem.title ?? string.Empty);
        lines.Add(separator);
        if (record.gak.HasKaraoke)
        {
            string lyrics = record.gak.karaokeLyrics.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\0', ' ');
            lines.AddRange(lyrics.Split('\n').Select(l => l.TrimEnd()));
        }
        return string.Join('\n', lines).TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// Applies one scorefix atomically. Lyrics are aligned against the current
    /// Japanese GAK karaoke; explicit parenthesised scorefix chunks retain the
    /// historical manual-override semantics. A mismatch throws before the
    /// record is modified.
    /// </summary>
    public static ScorefixDocument Apply(BBPRecord record, string text)
    {
        ArgumentNullException.ThrowIfNull(record);
        ScorefixDocument scorefix = ScorefixDocument.Parse(text);

        if (scorefix.TitleId is uint expectedId
            && record.mgrItem.titleID != 0xFFFFFFFF
            && record.mgrItem.titleID != expectedId)
        {
            throw new InvalidDataException(
                $"Scorefix is for title ID {expectedId:X8}, but the loaded song is {record.mgrItem.titleID:X8}.");
        }

        if (scorefix.Fingerprint is string expectedFingerprint && Fingerprint(record) != expectedFingerprint)
            throw new InvalidDataException("This scorefix is for a different set of Japanese lyrics than the loaded song's.");

        (string Lyrics, short[] Timer)? retimed = null;
        if (!string.IsNullOrEmpty(scorefix.Lyrics))
        {
            if (!record.gak.HasKaraoke)
                throw new InvalidDataException("This scorefix contains lyrics, but the loaded song has no karaoke data to retime.");
            retimed = Karaoke.RetimeLyrics(record.gak.karaokeLyrics, scorefix.Lyrics, record.gak.karaokeTimer);
        }

        // Commit only after parsing/alignment succeeded.
        record.gak.title = scorefix.Title;
        if (retimed is { } result)
        {
            // Parentheses are scorefix alignment markup, not display lyrics.
            record.gak.karaokeLyrics = result.Lyrics;
            record.gak.karaokeTimer = result.Timer;
        }

        var item = record.mgrItem;
        // The manager title keeps the scorefix's line breaks: the song card on the top screen draws them
        // (the game's own built-in entries have them, e.g. "Eine kleine \nNachtmusik"), and the song list
        // shows the title on one line without them (hardware-observed).
        item.title = scorefix.Title.Replace("\r", string.Empty);
        // Scorefix titles are Latin/romanised. titleSimple follows the new visible title, lowercased as
        // Nintendo writes it, so the song is filed under its letter rather than under symbols.
        item.titleSimple = JbMgr.Item.SimpleTitle(scorefix.FlatTitle);
        record.mgrItem = item;

        return scorefix;
    }
}
