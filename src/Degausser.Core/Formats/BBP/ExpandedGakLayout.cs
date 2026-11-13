using System.Buffers.Binary;

namespace Degausser;

/// <summary>
/// Nintendo's expanded 0x1BD58-byte GAK working layout. Playable-video packs
/// store this representation directly; ordinary BBP packs store the contracted
/// 0x14E3C-byte layout consumed by <see cref="Gak"/>.
/// </summary>
internal static class ExpandedGakLayout
{
    public const int BinarySize = 0x1BD58;
    public const uint Version = 0x00020002;

    private const int PackedChannelInfo = 0x020C;
    private const int ExpandedChannelInfo = 0x0210;
    private const int ChannelInfoSize = 48;
    private const int ChannelCount = 10;

    private const int PackedNotes = 0x03EC;
    private const int ExpandedNotes = 0x03F0;
    private const int PackedNotesPerChannel = 2400;
    private const int ExpandedNotesPerChannel = 3840;

    private const int PackedStrengths = 0x61AC;
    private const int ExpandedStrengths = 0x99F0;
    private const int PackedStrengthBytesPerChannel = 600 * sizeof(ushort);
    private const int ExpandedStrengthBytesPerChannel = 960 * sizeof(ushort);

    private const int PackedTempo = 0x908C;
    private const int ExpandedTempo = 0xE4F0;
    private const int PackedGuitarMetadata = 0xE77C;
    private const int ExpandedGuitarMetadata = 0x13BE0;
    private const int PackedGuitarMetadataSize = 2400 * sizeof(ushort);

    private const int PackedPianoPair = 0xFA3C;
    private const int ExpandedPianoPair = 0x159E0;
    private const int PackedKeyState = 0x12B0C;
    private const int ExpandedKeyState = 0x18AB0;
    private const int KeyStateCount = 11;
    private const int PackedKeyStateSize = 600;
    private const int ExpandedKeyStateSize = 960;

    private const int PackedAfterKeyState = 0x144D4;
    private const int ExpandedAfterKeyState = 0x1B3F0;

    /// <summary>
    /// Performs the inverse of the game's 0x14E3C -> 0x1BD58 editor expansion.
    /// Capacity-only tails are discarded and the line count is clamped exactly
    /// as Nintendo does when serializing the packed GAK.
    /// </summary>
    public static byte[] Contract(ReadOnlySpan<byte> expanded)
    {
        if (expanded.Length != BinarySize)
            throw new InvalidDataException($"Expanded GAK is 0x{expanded.Length:X} bytes; expected 0x{BinarySize:X}.");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(expanded);
        if (version != Version)
            throw new InvalidDataException($"Unsupported expanded GAK version 0x{version:X8}.");

        byte[] packed = new byte[Gak.BinarySize];
        Span<byte> destination = packed;

        // Everything through melodyChannel (+0x207) is unchanged.
        expanded[..0x208].CopyTo(destination);

        // Expanded +0x208 is u16; packed +0x208 is u8. The three following
        // bytes shift back by one, and ChannelInfo resumes on +0x20C.
        ushort linesPlus6 = BinaryPrimitives.ReadUInt16LittleEndian(expanded[0x208..]);
        byte timeSignature = expanded[0x20A];
        destination[0x208] = timeSignature switch
        {
            4 => (byte)Math.Min(linesPlus6, (ushort)156),
            3 => (byte)Math.Min(linesPlus6, (ushort)206),
            _ => 0
        };
        destination[0x209] = timeSignature;
        destination[0x20A] = expanded[0x20B];
        destination[0x20B] = expanded[0x20C];

        expanded.Slice(ExpandedChannelInfo, ChannelCount * ChannelInfoSize)
            .CopyTo(destination[PackedChannelInfo..]);

        for (int channel = 0; channel < ChannelCount; channel++)
        {
            expanded.Slice(
                    ExpandedNotes + channel * ExpandedNotesPerChannel,
                    PackedNotesPerChannel)
                .CopyTo(destination[(PackedNotes + channel * PackedNotesPerChannel)..]);

            expanded.Slice(
                    ExpandedStrengths + channel * ExpandedStrengthBytesPerChannel,
                    PackedStrengthBytesPerChannel)
                .CopyTo(destination[(PackedStrengths + channel * PackedStrengthBytesPerChannel)..]);
        }

        // Tempo through GuitarTimer is structurally unchanged after accounting
        // for the larger note/strength arrays.
        expanded.Slice(ExpandedTempo, ExpandedGuitarMetadata - ExpandedTempo)
            .CopyTo(destination[PackedTempo..]);

        // The live editor reserves 3840 u16 guitar-note metadata values; the
        // packed format serializes the first 2400.
        expanded.Slice(ExpandedGuitarMetadata, PackedGuitarMetadataSize)
            .CopyTo(destination[PackedGuitarMetadata..]);

        // Piano data, karaoke and timers are unchanged until KeyState.
        expanded.Slice(ExpandedPianoPair, ExpandedKeyState - ExpandedPianoPair)
            .CopyTo(destination[PackedPianoPair..]);

        for (int i = 0; i < KeyStateCount; i++)
        {
            expanded.Slice(
                    ExpandedKeyState + i * ExpandedKeyStateSize,
                    PackedKeyStateSize)
                .CopyTo(destination[(PackedKeyState + i * PackedKeyStateSize)..]);
        }

        // Chord-change tables are unchanged after the key-state capacity tail.
        expanded[ExpandedAfterKeyState..].CopyTo(destination[PackedAfterKeyState..]);

        // Packed GAK revision is (1, 2), i.e. u32 0x00020001.
        BinaryPrimitives.WriteUInt16LittleEndian(destination, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], 2);
        return packed;
    }
}
