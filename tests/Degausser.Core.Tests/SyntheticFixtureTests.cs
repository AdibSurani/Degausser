using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Degausser.Core.Tests;

/// <summary>
/// Golden-file tests around a deliberately synthetic eight-note scale. The
/// fixture was created from scratch for this repository and does not contain a
/// transcription of a copyrighted song.
/// </summary>
public class SyntheticFixtureTests
{
    // The golden BBP came from the 2026 Python oracle, which predates the
    // recovered Nintendo migration normalisation (master gain, karaoke forms).
    private static readonly BdxConversionOptions OracleOptions = new(NormaliseForP: false);

    private const string ExpectedTitle = "Degausser Test Scale";
    private const string ExpectedAuthor = "Synthetic";
    private const string BdxSha256 = "A1F1DC07A080F232D0A9B128D9A0FE0687EB45199762963E9D0A31575C09E665";
    private const string BbpSha256 = "DE2F4096721577D8BCD173CEBD13A8B78A902BD1EFA7CE5AD4706E4D814621F7";
    private const string GakSha256 = "4F2CB0020B1D96CC88E8CFA3F50958861F68B769AD3330694E12974C5F8BACBD";

    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", name);

    [Fact]
    public void FixtureFilesAreExactlyTheCommittedGoldenInputs()
    {
        byte[] bdx = File.ReadAllBytes(Fixture("degausser-test-scale.bdx"));
        byte[] bbp = File.ReadAllBytes(Fixture("degausser-test-scale.bbp"));

        Assert.Equal(BdxToBbpConverter.BdxSize, bdx.Length);
        Assert.Equal(697, bbp.Length);
        Assert.Equal(BdxSha256, Convert.ToHexString(SHA256.HashData(bdx)));
        Assert.Equal(BbpSha256, Convert.ToHexString(SHA256.HashData(bbp)));
    }

    [Fact]
    public void GoldenBbpReadsAsExpectedManagerAndScore()
    {
        byte[] bbp = File.ReadAllBytes(Fixture("degausser-test-scale.bbp"));
        var record = BBPRecord.FromBbp(bbp, "degausser-test-scale.bbp");

        Assert.Equal(ExpectedTitle, record.Title);
        Assert.Equal(ExpectedAuthor, record.Author);
        Assert.Equal(8, record.Lines);
        Assert.Equal(1, record.Instruments);
        Assert.False(record.HasKaraoke);
        Assert.False(record.HasGuitar);
        Assert.False(record.HasPiano);

        Assert.Equal(0x80008BCAu, record.mgrItem.titleID);
        Assert.Equal(0u, record.mgrItem.otherID);
        Assert.Equal(-1, (short)record.mgrItem.singer);
        Assert.Equal(JbMgr.Item.SimpleTitle(ExpectedTitle), record.mgrItem.titleSimple);
        Assert.Equal(50, record.mgrItem.scores.Length);
        Assert.All(record.mgrItem.scores, score => Assert.Equal((byte)0, score));

        Assert.Equal(BbpPackHeader.SupportedVersion, record.PackHeader.Version);
        Assert.Equal(1, record.PackHeader.GakPresent);
        Assert.Equal(Gak.BinarySize, record.PackHeader.GakUncompressedSize);
        Assert.Equal(BbpPackHeader.BinarySize, record.PackHeader.GakOffset);
        Assert.Equal(BbpPackHeader.UnknownWordCount, record.PackHeader.UnknownWords.Length);
        Assert.All(record.PackHeader.UnknownWords, value => Assert.Equal(0, value));

        Assert.Equal(0x00020001, record.gak.version);
        Assert.Equal(unchecked((int)0x80008BCAu), record.gak.titleID);
        Assert.Equal(ExpectedTitle, record.gak.title);
        Assert.Equal(14, record.gak.linesPlus6);
        Assert.Equal(4, record.gak.timeSignature);
        Assert.Equal((sbyte)100, record.gak.masterGain);
        Assert.Equal(10, record.gak.channelInfo.Length);
        Assert.Equal(10, record.gak.channelNotes.Length);
        Assert.Equal(128, record.gak.tempoChanges.Length);
        Assert.Equal(10, record.gak.volumeChanges.Length);
        Assert.Equal(Gak.BinarySize, record.gak.ToBytes().Length);
        Assert.Equal(GakSha256, Convert.ToHexString(SHA256.HashData(record.gak.ToBytes())));
    }

    [Fact]
    public void GoldenScoreContainsTheIntendedEightNoteScaleAndAutomation()
    {
        var record = BBPRecord.FromBBP(Fixture("degausser-test-scale.bbp"));
        var channel = record.gak.channelInfo[0];

        Assert.Equal(PlayType.Standard, channel.playType);
        Assert.Equal(1, channel.instrument); // Piano
        Assert.Equal(100, channel.volume);
        Assert.Equal((byte)8, channel.performanceParam07);
        Assert.Equal((byte)8, channel.performanceParam08);
        Assert.Equal(32, record.gak.pan[0].value);

        byte[] notes = record.gak.channelNotes[0].notes;
        byte[] starts = Enumerable.Range(0, 8).Select(i => notes[i * 4]).ToArray();
        Assert.Equal(new byte[] { 60, 62, 64, 65, 67, 69, 71, 72 }, starts);
        for (int i = 0; i < 8; i++)
            Assert.Equal(new byte[] { 0x80, 0x80, 0x80 }, notes.AsSpan(i * 4 + 1, 3).ToArray());

        Assert.Equal(0, record.gak.tempoChanges[0].time);
        Assert.Equal(120, record.gak.tempoChanges[0].value);
        Assert.Equal(-1, record.gak.tempoChanges[1].time);
        Assert.Equal(0, record.gak.volumeChanges[0].changes[0].time);
        Assert.Equal(127, record.gak.volumeChanges[0].changes[0].value);
        Assert.Equal(-1, record.gak.volumeChanges[0].changes[1].time);
    }

    [Fact]
    public void GoldenBbpContainerHeaderAndGzipPayloadAreConsistent()
    {
        byte[] bbp = File.ReadAllBytes(Fixture("degausser-test-scale.bbp"));
        ReadOnlySpan<byte> pack = bbp.AsSpan(JbMgr.Item.ItemBinarySize);

        Assert.Equal(0x20001, BinaryPrimitives.ReadInt32LittleEndian(pack[0..4]));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(pack[4..8]));
        Assert.Equal(Gak.BinarySize, BinaryPrimitives.ReadInt32LittleEndian(pack[8..12]));
        Assert.Equal(68, BinaryPrimitives.ReadInt32LittleEndian(pack[12..16]));
        int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(pack[16..20]);
        Assert.Equal(317, compressedLength);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(pack[20..24])); // no Vocaloid

        ReadOnlySpan<byte> gzip = pack.Slice(68, compressedLength);
        Assert.Equal(new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03 }, gzip[..10].ToArray());

        using var input = new MemoryStream(gzip.ToArray(), writable: false);
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zip.CopyTo(output);
        Assert.Equal(Gak.BinarySize, output.Length);
        Assert.Equal(GakSha256, Convert.ToHexString(SHA256.HashData(output.ToArray())));
    }

    [Fact]
    public void BdxConversionMatchesGoldenGakAndManagerItemByteForByte()
    {
        byte[] bdx = File.ReadAllBytes(Fixture("degausser-test-scale.bdx"));
        byte[] bbp = File.ReadAllBytes(Fixture("degausser-test-scale.bbp"));
        var converted = BdxToBbpConverter.Convert(bdx, OracleOptions);

        Assert.Empty(converted.Warnings);
        Assert.Equal(bbp.AsSpan(0, JbMgr.Item.ItemBinarySize).ToArray(), converted.ManagerItem.ToBytes());

        var golden = BBPRecord.FromBbp(bbp);
        Assert.Equal(golden.gak.ToBytes(), converted.RawGak);
        Assert.Equal(golden.gak.ToBytes(), converted.Gak.ToBytes());
    }

    [Fact]
    public void BdxConversionProducesTheExactGoldenBbpContents()
    {
        byte[] bdx = File.ReadAllBytes(Fixture("degausser-test-scale.bdx"));
        byte[] expected = File.ReadAllBytes(Fixture("degausser-test-scale.bbp"));

        var record = BBPRecord.FromBdx(bdx, "degausser-test-scale.bdx", OracleOptions);
        byte[] actual = record.ToBbpBytes();

        var expectedRecord = BBPRecord.FromBbp(expected);
        var actualRecord = BBPRecord.FromBbp(actual);

        Assert.Equal(expectedRecord.mgrItem.ToBytes(), actualRecord.mgrItem.ToBytes());
        Assert.Equal(expectedRecord.gak.ToBytes(), actualRecord.gak.ToBytes());
        Assert.Equal(expectedRecord.vocaloid, actualRecord.vocaloid);
    }

    [Fact]
    public void GoldenBbpRoundTripsWithoutChangingItsContents()
    {
        byte[] expected = File.ReadAllBytes(Fixture("degausser-test-scale.bbp"));

        var original = BBPRecord.FromBbp(expected);
        byte[] recompiledBytes = original.ToBbpBytes();
        var recompiled = BBPRecord.FromBbp(recompiledBytes);

        Assert.Equal(original.mgrItem.ToBytes(), recompiled.mgrItem.ToBytes());
        Assert.Equal(original.gak.ToBytes(), recompiled.gak.ToBytes());
        Assert.Equal(original.vocaloid, recompiled.vocaloid);
    }

    [Fact]
    public void UnknownPackHeaderWordsSurviveARecompile()
    {
        byte[] bbp = File.ReadAllBytes(Fixture("degausser-test-scale.bbp"));
        int unknownOffset = JbMgr.Item.ItemBinarySize + (9 * sizeof(int));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            bbp.AsSpan(unknownOffset, sizeof(int)), unchecked((int)0xA1B2C3D4));

        BBPRecord record = BBPRecord.FromBbp(bbp);
        Assert.Equal(unchecked((int)0xA1B2C3D4), record.PackHeader.UnknownWords[0]);

        byte[] recompiledPack = record.Recompile();
        BbpPackHeader reread = BbpPackHeader.Read(recompiledPack);
        Assert.Equal(unchecked((int)0xA1B2C3D4), reread.UnknownWords[0]);
        Assert.Equal(record.PackHeader.UnknownWords, reread.UnknownWords);
    }

}
