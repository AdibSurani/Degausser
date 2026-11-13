namespace Degausser;

/// <summary>
/// Lightweight metadata extracted from the prefix of a Band Brothers DX BDX.
/// Library indexing should use this rather than converting the complete score
/// into BBP/GAK just to obtain list-view information.
/// </summary>
public sealed record BdxMetadata(
    string Title,
    string Contributor,
    int Parts,
    bool HasKaraoke,
    bool HasGuitar,
    bool HasPiano,
    bool HasDrums);

public static class BdxMetadataReader
{
    // Everything needed by the library ends at the 32-byte contributor field.
    public const int PrefixSize = 0x016C;

    private const int Labels = 0x0048;
    private const int HasKaraoke = 0x00AF;
    private const int ChannelInfo = 0x00C8;
    private const int Contributor = 0x014C;

    private const byte PlayDrum = 1;
    private const byte PlayGuitar = 2;
    private const byte PlayPiano = 3;

    public static BdxMetadata Read(ReadOnlySpan<byte> prefix, string? defaultAuthor = null)
    {
        if (prefix.Length < PrefixSize)
            throw new InvalidDataException($"BDX metadata requires at least 0x{PrefixSize:X} bytes; got 0x{prefix.Length:X}.");

        string title = string.Concat(
            Language.GetBDXJPString(prefix.Slice(Labels, 32)),
            Language.GetBDXJPString(prefix.Slice(Labels + 32, 32)),
            Language.GetBDXJPString(prefix.Slice(Labels + 64, 32)))
            .Replace("\n", string.Empty);

        string contributor = Language.GetBDXJPString(prefix.Slice(Contributor, 32));

        int parts = 0;
        bool guitar = false;
        bool piano = false;
        bool drums = false;

        for (int channel = 0; channel < 8; channel++)
        {
            int offset = ChannelInfo + 16 * channel;
            if (prefix[offset + 2] != 0)
                parts++;

            switch (prefix[offset + 3])
            {
                case PlayDrum: drums = true; break;
                case PlayGuitar: guitar = true; break;
                case PlayPiano: piano = true; break;
            }
        }

        return new BdxMetadata(
            title,
            string.IsNullOrEmpty(contributor) ? BdxToBbpConverter.DefaultAuthorFor(defaultAuthor) : contributor,
            parts,
            prefix[HasKaraoke] != 0,
            guitar,
            piano,
            drums);
    }
}
