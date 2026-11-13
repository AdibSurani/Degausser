using System.Buffers.Binary;
using System.IO.Compression;

namespace Degausser.Core.Tests;

public class BBPRecordTests
{
    [Fact]
    public void BdxCanRoundTripThroughBbpContainer()
    {
        byte[] bdx = BdxConverterTests.CreateMinimalBdx();
        var record = BBPRecord.FromBdx(bdx, "synthetic.bdx");
        byte[] expectedGak = record.gak.ToBytes();

        byte[] bbp = record.ToBbpBytes();
        var reread = BBPRecord.FromBbp(bbp, "synthetic.bbp");

        Assert.Equal(record.mgrItem.titleID, reread.mgrItem.titleID);
        Assert.Equal(expectedGak, reread.gak.ToBytes());
        Assert.Equal(64, record.ContentHash.Length);
        Assert.Matches("^[0-9A-F]{64}$", record.ContentHash);
    }


    [Fact]
    public void PlayableVideoPackCanLoadItsExpandedScore()
    {
        const int expandedSize = 0x1BD58;
        const int payloadOffset = 8 + (7 * 16);

        byte[] expanded = new byte[expandedSize];
        BinaryPrimitives.WriteUInt32LittleEndian(expanded, 0x00020002);
        BinaryPrimitives.WriteInt32LittleEndian(expanded.AsSpan(4), unchecked((int)0x900027E5));
        BinaryPrimitives.WriteUInt16LittleEndian(expanded.AsSpan(0x208), 58);
        expanded[0x20A] = 4;

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                gzip.Write(expanded);
            compressed = output.ToArray();
        }

        byte[] wrapper = new byte[payloadOffset + compressed.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper, 0x00010003);
        BinaryPrimitives.WriteInt32LittleEndian(wrapper.AsSpan(4), 7);
        BinaryPrimitives.WriteInt32LittleEndian(wrapper.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wrapper.AsSpan(12), expandedSize);
        BinaryPrimitives.WriteInt32LittleEndian(wrapper.AsSpan(16), payloadOffset);
        BinaryPrimitives.WriteInt32LittleEndian(wrapper.AsSpan(20), compressed.Length);
        compressed.CopyTo(wrapper, payloadOffset);

        BBPRecord record = BBPRecord.FromPack(wrapper, "movie-pack");

        Assert.Equal(0x900027E5u, unchecked((uint)record.gak.titleID));
        Assert.Equal(58, record.gak.linesPlus6);
        Assert.Equal(4, record.gak.timeSignature);
        byte[] packed = record.gak.ToBytes();
        Assert.Equal(Gak.BinarySize, packed.Length);
        Assert.Equal(0x00020001u, BinaryPrimitives.ReadUInt32LittleEndian(packed));
    }

    [Fact]
    public void CorruptPackHeaderIsRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BBPRecord.FromPack(new byte[68]));
        Assert.Contains("version", ex.Message.ToLowerInvariant());
    }
}
