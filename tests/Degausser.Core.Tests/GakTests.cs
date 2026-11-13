using System.Text;

namespace Degausser.Core.Tests;

public class GakTests
{
    [Fact]
    public void BinarySizeMatchesRecoveredLayout()
        => Assert.Equal(0x14E3C, Gak.BinarySize);

    [Fact]
    public void FixedLayoutRoundTripsByteForByteWhenStringsContainNoEmbeddedNulls()
    {
        byte[] raw = new byte[Gak.BinarySize];
        var random = new Random(0xD3A6A55);
        random.NextBytes(raw);

        // These fields historically used ByValTStr, so valid serialized data
        // reserves the final UTF-16 code unit for NUL. Fill every usable code
        // unit while retaining that terminator for a strict raw round-trip.
        FillUtf16Terminated(raw.AsSpan(0x10, 500), 'A');
        FillUtf16Terminated(raw.AsSpan(0xFC2C, 6000), '歌');

        var gak = Gak.Read(raw);
        byte[] roundTrip = gak.ToBytes();

        Assert.Equal(raw, roundTrip);
    }


    [Fact]
    public void GuitarMetadataIsExposedAsOneUshortPerNoteByte()
    {
        byte[] raw = new byte[Gak.BinarySize];
        const int offset = 0xE77C;
        raw[offset + 0] = 0x34;
        raw[offset + 1] = 0x92;
        raw[offset + 2] = 0xFF;
        raw[offset + 3] = 0x00;

        Gak gak = Gak.Read(raw);

        Assert.Equal(2400, gak.guitarNoteMetadata.Length);
        Assert.Equal((ushort)0x9234, gak.guitarNoteMetadata[0]);
        Assert.Equal((ushort)0x00FF, gak.guitarNoteMetadata[1]);
        Assert.Equal(raw, gak.ToBytes());
    }

    [Fact]
    public void FixedStringsPreserveLegacyByValTStrCapacity()
    {
        var gak = new Gak
        {
            title = new string('T', 250),
            karaokeLyrics = new string('歌', 3000)
        };

        byte[] bytes = gak.ToBytes();
        var reread = Gak.Read(bytes);

        Assert.Equal(249, reread.title.Length);
        Assert.Equal(2999, reread.karaokeLyrics.Length);
        Assert.Equal(new byte[] { 0, 0 }, bytes.AsSpan(0x10 + 498, 2).ToArray());
        Assert.Equal(new byte[] { 0, 0 }, bytes.AsSpan(0xFC2C + 5998, 2).ToArray());
    }

    private static void FillUtf16Terminated(Span<byte> bytes, char value)
    {
        bytes.Clear();
        for (int i = 0; i + 2 < bytes.Length; i += 2)
        {
            bytes[i] = (byte)value;
            bytes[i + 1] = (byte)(value >> 8);
        }
    }
}
