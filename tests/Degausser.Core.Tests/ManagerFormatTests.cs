namespace Degausser.Core.Tests;

/// <summary>
/// Synthetic layout/round-trip coverage for manager formats for which a real
/// extdata fixture has not yet been added. Sizes and field order come from the
/// pre-modernisation Degausser declarations; real-file golden tests can be
/// added without changing these tests later.
/// </summary>
public class ManagerFormatTests
{
    [Fact]
    public void JbMgr2LayoutMatchesLegacyDeclaration()
    {
        Assert.Equal(320, JbMgr2.Item.ItemBinarySize);
        Assert.Equal(1500, JbMgr2.ItemCount);
        Assert.Equal(480_008, JbMgr2.BinarySize);
        Assert.Equal(0x4A425632, JbMgr2.CreateEmpty().magic);
    }

    [Fact]
    public void JbMgr2ItemRoundTripsAllKnownAndUnknownFields()
    {
        var item = JbMgr2.Item.CreateEmpty();
        item.item.titleID = 0x81234567;
        item.item.title = "Secondary";
        item.item.author = "Degausser";
        item.item.scores[7] = 0x67;
        item.unk1 = 0xA1B2C3D4;
        item.unk2 = 0x10203040;

        byte[] bytes = item.ToBytes();
        var reread = JbMgr2.Item.Read(bytes);

        Assert.Equal(JbMgr2.Item.ItemBinarySize, bytes.Length);
        Assert.Equal(item.item.titleID, reread.item.titleID);
        Assert.Equal(item.item.title, reread.item.title);
        Assert.Equal(item.item.author, reread.item.author);
        Assert.Equal(item.item.scores, reread.item.scores);
        Assert.Equal(item.unk1, reread.unk1);
        Assert.Equal(item.unk2, reread.unk2);
        Assert.Equal(bytes, reread.ToBytes());
    }

    [Fact]
    public void SingerMgrLayoutMatchesLegacyDeclaration()
    {
        Assert.Equal(412, SingerMgr.Item.ItemBinarySize);
        Assert.Equal(1011, SingerMgr.ItemCount);
        Assert.Equal(416_536, SingerMgr.BinarySize);
    }

    [Fact]
    public void SingerMgrItemRoundTripsAndPreservesByValTStrSemantics()
    {
        var item = SingerMgr.Item.CreateEmpty();
        item.unk1 = 0x11223344;
        item.unk2 = 0x55667788;
        item.unk3 = -1234;
        item.unk4 = 2345;
        item.artist = "12345678"; // legacy SizeConst=8 => 7 chars + NUL
        item.unk5 = 1;
        item.unk6 = 2;
        item.unk7 = 3;
        item.unk8 = 4;
        item.unk9 = 5;
        item.unk10 = 6;
        item.greeting = new string('G', 180); // SizeConst=180 => 179 + NUL

        byte[] bytes = item.ToBytes();
        var reread = SingerMgr.Item.Read(bytes);

        Assert.Equal(SingerMgr.Item.ItemBinarySize, bytes.Length);
        Assert.Equal("1234567", reread.artist);
        Assert.Equal(new string('G', 179), reread.greeting);
        Assert.Equal(item.unk1, reread.unk1);
        Assert.Equal(item.unk10, reread.unk10);
        Assert.Equal(bytes, reread.ToBytes());
    }

    [Fact]
    public void SingerMgr2LayoutMatchesLegacyDeclaration()
    {
        Assert.Equal(476, SingerMgr2.Item.ItemBinarySize);
        Assert.Equal(1011, SingerMgr2.ItemCount);
        Assert.Equal(481_240, SingerMgr2.BinarySize);
    }

    [Fact]
    public void SingerMgr2ItemRoundTripsAllEightUnknownUlongs()
    {
        var item = SingerMgr2.Item.CreateEmpty();
        item.item.artist = "Adib";
        item.item.greeting = "Synthetic singer fixture";
        item.unk1 = 0x0102030405060708UL;
        item.unk2 = 0x1112131415161718UL;
        item.unk3 = 0x2122232425262728UL;
        item.unk4 = 0x3132333435363738UL;
        item.unk5 = 0x4142434445464748UL;
        item.unk6 = 0x5152535455565758UL;
        item.unk7 = 0x6162636465666768UL;
        item.unk8 = 0x7172737475767778UL;

        byte[] bytes = item.ToBytes();
        var reread = SingerMgr2.Item.Read(bytes);

        Assert.Equal(SingerMgr2.Item.ItemBinarySize, bytes.Length);
        Assert.Equal(item.item.artist, reread.item.artist);
        Assert.Equal(item.item.greeting, reread.item.greeting);
        Assert.Equal(item.unk1, reread.unk1);
        Assert.Equal(item.unk2, reread.unk2);
        Assert.Equal(item.unk3, reread.unk3);
        Assert.Equal(item.unk4, reread.unk4);
        Assert.Equal(item.unk5, reread.unk5);
        Assert.Equal(item.unk6, reread.unk6);
        Assert.Equal(item.unk7, reread.unk7);
        Assert.Equal(item.unk8, reread.unk8);
        Assert.Equal(bytes, reread.ToBytes());
    }


    [Fact]
    public void MvMgrLayoutMatchesLegacyDeclaration()
    {
        Assert.Equal(392, MvMgr.Item.ItemBinarySize);
        Assert.Equal(1000, MvMgr.ItemCount);
        Assert.Equal(392_008, MvMgr.BinarySize);
    }

    [Fact]
    public void MvMgrItemRoundTripsFixedStringsAndIds()
    {
        var item = MvMgr.Item.CreateEmpty();
        item.hasItem = 1;
        item.videoID = 0x81234567;
        item.title = "Synthetic movie";
        item.description = "EndianBinaryIO round-trip fixture";

        byte[] bytes = item.ToBytes();
        var reread = MvMgr.Item.Read(bytes);

        Assert.Equal(MvMgr.Item.ItemBinarySize, bytes.Length);
        Assert.Equal(item.hasItem, reread.hasItem);
        Assert.Equal(item.videoID, reread.videoID);
        Assert.Equal(item.title, reread.title);
        Assert.Equal(item.description, reread.description);
        Assert.Equal(bytes, reread.ToBytes());
    }


    [FixtureFact("DEGAUSSER_JBMGR_FIXTURE")]
    public void RealJbMgrFixtureRoundTripsByteForByte()
        => AssertExternalRoundTrip("DEGAUSSER_JBMGR_FIXTURE", JbMgr.BinarySize, bytes => JbMgr.Read(bytes).ToBytes());

    [FixtureFact("DEGAUSSER_JBMGR2_FIXTURE")]
    public void RealJbMgr2FixtureRoundTripsByteForByte()
        => AssertExternalRoundTrip("DEGAUSSER_JBMGR2_FIXTURE", JbMgr2.BinarySize, bytes => JbMgr2.Read(bytes).ToBytes());

    [FixtureFact("DEGAUSSER_SINGERMGR_FIXTURE")]
    public void RealSingerMgrFixtureRoundTripsByteForByte()
        => AssertExternalRoundTrip("DEGAUSSER_SINGERMGR_FIXTURE", SingerMgr.BinarySize, bytes => SingerMgr.Read(bytes).ToBytes());

    [FixtureFact("DEGAUSSER_SINGERMGR2_FIXTURE")]
    public void RealSingerMgr2FixtureRoundTripsByteForByte()
        => AssertExternalRoundTrip("DEGAUSSER_SINGERMGR2_FIXTURE", SingerMgr2.BinarySize, bytes => SingerMgr2.Read(bytes).ToBytes());

    [FixtureFact("DEGAUSSER_MVMGR_FIXTURE")]
    public void RealMvMgrFixtureRoundTripsByteForByte()
        => AssertExternalRoundTrip("DEGAUSSER_MVMGR_FIXTURE", MvMgr.BinarySize, bytes => MvMgr.Read(bytes).ToBytes());

    private static void AssertExternalRoundTrip(string environmentVariable, int expectedSize, Func<byte[], byte[]> roundTrip)
    {
        byte[] input = File.ReadAllBytes(FixtureFactAttribute.GetPath(environmentVariable));
        Assert.Equal(expectedSize, input.Length);
        Assert.Equal(input, roundTrip(input));
    }

    [Fact]
    public void EmptyManagersRoundTripAtTheirExactFixedSizes()
    {
        byte[] jb2 = JbMgr2.CreateEmpty().ToBytes();
        byte[] singer = SingerMgr.CreateEmpty().ToBytes();
        byte[] singer2 = SingerMgr2.CreateEmpty().ToBytes();
        byte[] mv = MvMgr.CreateEmpty().ToBytes();

        Assert.Equal(JbMgr2.BinarySize, jb2.Length);
        Assert.Equal(SingerMgr.BinarySize, singer.Length);
        Assert.Equal(SingerMgr2.BinarySize, singer2.Length);
        Assert.Equal(MvMgr.BinarySize, mv.Length);
        Assert.Equal(jb2, JbMgr2.Read(jb2).ToBytes());
        Assert.Equal(singer, SingerMgr.Read(singer).ToBytes());
        Assert.Equal(singer2, SingerMgr2.Read(singer2).ToBytes());
        Assert.Equal(mv, MvMgr.Read(mv).ToBytes());
    }
}
