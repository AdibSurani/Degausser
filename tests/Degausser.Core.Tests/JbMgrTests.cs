using System.Buffers.Binary;
using System.Text;

namespace Degausser.Core.Tests;

public class JbMgrTests
{
    [Fact]
    public void LayoutConstantsMatchReverseEngineeredFormat()
    {
        Assert.Equal(8, JbMgr.HeaderSize);
        Assert.Equal(3700, JbMgr.ItemCount);
        Assert.Equal(312, JbMgr.Item.ItemBinarySize);
        Assert.Equal(1_154_408, JbMgr.BinarySize);
    }

    [Fact]
    public void EmptyManagerDoesNotShareScoreArrays()
    {
        var mgr = JbMgr.CreateEmpty();
        mgr.items[0].scores[0] = 0x67;

        Assert.Equal(0x67, mgr.items[0].scores[0]);
        Assert.Equal(0, mgr.items[1].scores[0]);
        Assert.NotSame(mgr.items[0].scores, mgr.items[1].scores);
    }

    [Fact]
    public void EmptyManagerRoundTripsByteForByte()
    {
        byte[] first = JbMgr.CreateEmpty().ToBytes();
        byte[] second = JbMgr.Read(first).ToBytes();

        Assert.Equal(JbMgr.BinarySize, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ItemSerializesAtKnownOffsetsAndRoundTrips()
    {
        var item = JbMgr.Item.CreateEmpty();
        item.titleID = 0x81234567;
        item.otherID = 0x10203040;
        item.flags = new JbMgr.Item.JbFlags { flag = 0x12345678 };
        item.singer = JbMgr.Item.JbSinger.OriginalFemale;
        item.icon = JbMgr.Item.JbIcon.Music;
        item.title = new string('T', 51); // legacy ByValTStr reserves the final code unit for NUL
        item.titleSimple = "Simple";
        item.author = "Author";
        item.videoID = 0xA1B2C3D4;
        item.scores = Enumerable.Range(0, 50).Select(i => (byte)i).ToArray();
        item.zeroPadding = unchecked((short)0x55AA);

        byte[] bytes = item.ToBytes();

        Assert.Equal(0x81234567u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.Equal(new string('T', 50) + "\0", Encoding.Unicode.GetString(bytes.AsSpan(16, 102)));
        Assert.StartsWith("Simple", Encoding.Unicode.GetString(bytes.AsSpan(118, 102)));
        Assert.StartsWith("Author", Encoding.Unicode.GetString(bytes.AsSpan(220, 36)));
        Assert.Equal(0xA1B2C3D4u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(256, 4)));
        Assert.Equal(item.scores, bytes.AsSpan(260, 50).ToArray());
        Assert.Equal(item.zeroPadding, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(310, 2)));

        var reread = JbMgr.Item.Read(bytes);
        Assert.Equal(item.titleID, reread.titleID);
        Assert.Equal(item.flags.flag, reread.flags.flag);
        Assert.Equal(new string('T', 50), reread.title);
        Assert.Equal(item.titleSimple, reread.titleSimple);
        Assert.Equal(item.author, reread.author);
        Assert.Equal(item.scores, reread.scores);
        Assert.Equal(bytes, reread.ToBytes());
    }

    [Fact]
    public void PartsUsesAllFiveNativeBits()
    {
        var flags = new JbMgr.Item.JbFlags { flag = 0x25A50000 };
        flags.Parts = 31;

        Assert.Equal(31, flags.Parts);
        Assert.Equal(0x25A500F8, flags.flag);
    }
}
