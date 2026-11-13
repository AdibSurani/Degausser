using System.Buffers.Binary;

namespace Degausser;

/// <summary>
/// In-place edits of the 0x00010003 "playable-video" song pack. <see cref="BBPRecord.Recompile"/>
/// would canonicalise such a pack to the ordinary 0x00020001 form, which the game's
/// movie player is not known to accept, so the title and karaoke are patched into the
/// expanded score and every other payload is carried over byte-for-byte.
/// </summary>
internal static class PlayableVideoPack
{
    public const uint Version = 0x00010003;

    private const int DescriptorTable = 8;
    private const int DescriptorSize = 16;
    private const int DescriptorCount = 7;

    // Packed-GAK offsets of the editable fields and where the expansion put them
    // (header fields are unmoved; piano data, karaoke and timers shift by 0x5FA4).
    private const int Title = 0x10, TitleSize = 500;
    private const int Lyrics = 0xFC2C, LyricsSize = 6000;
    private const int Timer = 0x1139C, TimerSize = 6000;
    private const int KaraokeShift = 0x159E0 - 0xFA3C;

    public static bool Is(ReadOnlySpan<byte> pack)
        => pack.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(pack) == Version;

    /// <summary>Returns <paramref name="pack"/> with the title, lyrics and timers of
    /// <paramref name="gak"/> written into its score payload.</summary>
    public static byte[] WithScoreText(byte[] pack, Gak gak)
    {
        if (!Is(pack) || BinaryPrimitives.ReadInt32LittleEndian(pack.AsSpan(4)) != DescriptorCount)
            throw new InvalidDataException("Not a playable-video pack.");

        int offset = BinaryPrimitives.ReadInt32LittleEndian(pack.AsSpan(DescriptorTable + 8));
        int stored = BinaryPrimitives.ReadInt32LittleEndian(pack.AsSpan(DescriptorTable + 12));
        if (BinaryPrimitives.ReadInt32LittleEndian(pack.AsSpan(DescriptorTable)) != 1
            || offset < DescriptorTable + DescriptorCount * DescriptorSize || stored <= 0 || offset > pack.Length - stored)
            throw new InvalidDataException("Playable-video score descriptor is invalid.");

        byte[] expanded = pack.AsSpan(offset, stored).ToArray().Decompress();
        if (expanded.Length != ExpandedGakLayout.BinarySize)
            throw new InvalidDataException($"Playable-video score expands to 0x{expanded.Length:X}.");

        byte[] packed = gak.ToBytes();
        packed.AsSpan(Title, TitleSize).CopyTo(expanded.AsSpan(Title));
        packed.AsSpan(Lyrics, LyricsSize).CopyTo(expanded.AsSpan(Lyrics + KaraokeShift));
        packed.AsSpan(Timer, TimerSize).CopyTo(expanded.AsSpan(Timer + KaraokeShift));

        byte[] score = expanded.Compress();
        int oldEnd = Math.Min(offset + ((stored + 3) & ~3), pack.Length);
        int newEnd = offset + ((score.Length + 3) & ~3);
        int delta = newEnd - oldEnd;

        byte[] result = new byte[newEnd + (pack.Length - oldEnd)];
        pack.AsSpan(0, offset).CopyTo(result);
        score.CopyTo(result, offset);
        pack.AsSpan(oldEnd).CopyTo(result.AsSpan(oldEnd + delta));

        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(DescriptorTable + 12), score.Length);
        for (int i = 1; i < DescriptorCount; i++)
        {
            int at = DescriptorTable + i * DescriptorSize + 8;
            int payloadOffset = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(at));
            if (payloadOffset >= oldEnd)
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), payloadOffset + delta);
        }
        return result;
    }
}
