using System.Buffers.Binary;

namespace Degausser.Core.Tests;

public class BdxConverterTests
{
    private const int ChannelInfo = 0x00C8;
    private const int LinesPlus6 = 0x00AB;
    private const int MainInstrument = 0x00AE;
    private const int MasterVolume = 0x00B7;
    private const int KeyBlocks = 0x6D18;

    // Every pair below was observed in Nintendo's own DX -> P migrations.
    [Theory]
    [InlineData(-63, -62)]
    [InlineData(-32, -62)]
    [InlineData(-30, -57)]
    [InlineData(-26, -57)]
    [InlineData(-24, -47)]
    [InlineData(-19, -47)]
    [InlineData(-18, -32)]
    [InlineData(-13, -32)]
    [InlineData(-12, -20)]
    [InlineData(-7, -20)]
    [InlineData(-6, 0)]
    [InlineData(0, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 20)]
    [InlineData(12, 20)]
    [InlineData(13, 32)]
    [InlineData(18, 32)]
    [InlineData(19, 47)]
    [InlineData(24, 47)]
    [InlineData(26, 57)]
    [InlineData(30, 57)]
    [InlineData(32, 62)]
    [InlineData(57, 62)]
    public void PanMappingMatchesNintendosMigration(int source, int expected)
        => Assert.Equal(expected, BdxToBbpConverter.MapPan(source));

    [Fact]
    public void CustomBdxTextDecoderHandlesAsciiAndTwoByteCharacters()
    {
        Assert.Equal("ABC", Language.GetBDXJPString([0x41, 0x42, 0x43, 0x00]));
        Assert.Equal("ガ", Language.GetBDXJPString([0x80, 0x00, 0x00]));
        Assert.Equal(string.Empty, Language.GetBDXJPString([0x10, 0x00]));          // DX empty title line
        Assert.Equal("D-Tecnolife", Language.GetBDXJPString("D-Tecnolife\u0010\0"u8));
    }

    [Fact]
    public void BlankContributorGetsTheDefaultOrChosenAuthor()
    {
        byte[] bdx = CreateMinimalBdx();
        Assert.Equal("Degausser 3.0", BdxToBbpConverter.Convert(bdx).ManagerItem.author);
        Assert.Equal("My Name", BdxToBbpConverter.Convert(bdx, new BdxConversionOptions(DefaultAuthor: " My Name ")).ManagerItem.author);
        Assert.Equal("0123456789ABCDEFG", BdxToBbpConverter.Convert(
            bdx, new BdxConversionOptions(DefaultAuthor: "0123456789ABCDEFGHIJ")).ManagerItem.author);
        Assert.Equal("Forced", BdxToBbpConverter.Convert(
            bdx, new BdxConversionOptions(Author: "Forced", DefaultAuthor: "Other")).ManagerItem.author);
    }

    [Fact]
    public void MinimalBdxConvertsToCanonicalPLayout()
    {
        byte[] bdx = CreateMinimalBdx();
        var result = BdxToBbpConverter.Convert(bdx);
        byte[] g = result.RawGak;

        Assert.Equal(Gak.BinarySize, g.Length);
        Assert.Equal(0x00020001u, BinaryPrimitives.ReadUInt32LittleEndian(g.AsSpan(0, 4)));
        Assert.Equal(BdxToBbpConverter.CreateDeterministicTitleId(bdx), BinaryPrimitives.ReadUInt32LittleEndian(g.AsSpan(4, 4)));
        Assert.Equal("TEST", result.Gak.title);
        Assert.Equal(20, BinaryPrimitives.ReadInt16LittleEndian(g.AsSpan(0xE28C + 2, 2))); // Nintendo pan bucket 7..12
        Assert.Equal(-1, BinaryPrimitives.ReadInt16LittleEndian(g.AsSpan(0x61AC, 2)));
        Assert.Equal(8, g[0x20C + 7]);
        Assert.Equal(8, g[0x20C + 8]);
        Assert.Equal(-1, BinaryPrimitives.ReadInt16LittleEndian(g.AsSpan(0x928C + 8 * 256 * 4, 2)));
        Assert.Equal(0x80, g[0x12B0C]);
        Assert.Equal(0x80, g[0x12B0C + 8 * 600]);
        Assert.Equal(g, result.Gak.ToBytes());
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(g.AsSpan(4, 4)), result.ManagerItem.titleID);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void DrumBdxUsesNintendoPerformanceParam08Default()
    {
        byte[] bdx = CreateMinimalBdx();
        bdx[ChannelInfo + 3] = 1; // Drum

        byte[] g = BdxToBbpConverter.Convert(bdx).RawGak;

        Assert.Equal(8, g[0x20C + 7]);
        Assert.Equal(0, g[0x20C + 8]);
    }

    [Fact]
    public void ThreeFourBdxSetsRecoveredCatalogueMeterBit()
    {
        byte[] bdx = CreateMinimalBdx();
        bdx[0x00AA] = 3;

        BdxConversionResult converted = BdxToBbpConverter.Convert(bdx);

        Assert.Equal((byte)3, converted.Gak.timeSignature);
        Assert.True(converted.ManagerItem.flags.IsThreeFourMeter);
    }

    [Fact]
    public void LightweightMetadataMatchesFullConversion()
    {
        byte[] bdx = CreateMinimalBdx();
        "CONTRIBUTOR"u8.CopyTo(bdx.AsSpan(0x014C));
        bdx[0x00AF] = 1;
        bdx[ChannelInfo + 3] = 2; // Guitar

        BdxMetadata metadata = BdxMetadataReader.Read(bdx.AsSpan(0, BdxMetadataReader.PrefixSize));
        BdxConversionResult converted = BdxToBbpConverter.Convert(bdx);

        Assert.Equal(converted.ManagerItem.title, metadata.Title);
        Assert.Equal(converted.ManagerItem.author, metadata.Contributor);
        Assert.Equal(converted.ManagerItem.flags.Parts, metadata.Parts);
        Assert.Equal(converted.ManagerItem.flags.HasLyrics, metadata.HasKaraoke);
        Assert.Equal(converted.ManagerItem.flags.HasGuitar, metadata.HasGuitar);
        Assert.Equal(converted.ManagerItem.flags.HasPiano, metadata.HasPiano);
        Assert.Equal(converted.ManagerItem.flags.HasDrum, metadata.HasDrums);
    }

    [Fact]
    public void MetadataReaderNeedsOnlyTheBdxPrefix()
    {
        byte[] bdx = CreateMinimalBdx();
        BdxMetadata metadata = BdxMetadataReader.Read(bdx.AsSpan(0, BdxMetadataReader.PrefixSize));
        Assert.Equal("TEST", metadata.Title);
        Assert.Equal(1, metadata.Parts);
    }


    [Fact]
    public void DxRomExactPianoThreeVoiceSeventhDropsTheLowestGeneratedTone()
    {
        byte[] bdx = CreatePianoBdx(quality: 2, rawRoot: 0x00, highest: 70, voicing: 0); // C7, 3-close

        byte[] g = BdxToBbpConverter.Convert(
            bdx,
            new BdxConversionOptions(PianoChordEncoding: BdxPianoChordEncoding.ExactLiteral)).RawGak;

        Assert.Equal((byte)0x00, g[0xFA3C]);
        Assert.Equal((byte)0xFF, g[0xFA3D]);
        Assert.Equal(new byte[] { 70, 67, 64, 0 }, g.AsSpan(0xFB2C, 4).ToArray());
    }

    [Fact]
    public void DxRomExactPianoMediumCanUseNativePDropDescriptor()
    {
        byte[] bdx = CreatePianoBdx(quality: 0, rawRoot: 0x00, highest: 70, voicing: 1); // C major, 3-medium

        byte[] g = BdxToBbpConverter.Convert(bdx).RawGak;

        // P native C major, centred rotation, 3 voices, drop transform.
        Assert.Equal((byte)0x07, g[0xFA3C]);
        Assert.Equal((byte)0x11, g[0xFA3D]);
    }

    [Fact]
    public void DxRomExactPianoWideFallsBackToLiteralWithoutChangingPitches()
    {
        byte[] bdx = CreatePianoBdx(quality: 0, rawRoot: 0x00, highest: 70, voicing: 2); // C major, 3-wide

        byte[] g = BdxToBbpConverter.Convert(bdx).RawGak;

        Assert.Equal((byte)0x00, g[0xFA3C]);
        Assert.Equal((byte)0xFF, g[0xFA3D]);
        Assert.Equal(new byte[] { 67, 48, 40, 0 }, g.AsSpan(0xFB2C, 4).ToArray());
    }

    [Fact]
    public void DxRomCustomPianoFlagAppliesGlobalVoicingBeforePStorage()
    {
        byte[] bdx = CreateMinimalBdx();
        bdx[ChannelInfo + 3] = 3;
        bdx.AsSpan(0x6608, 64).Fill(0xFF);
        bdx[0x6608] = 0;
        bdx[0x6609] = 0xFF;
        new byte[] { 60, 64, 67, 72 }.CopyTo(bdx, 0x6648);
        BinaryPrimitives.WriteUInt32LittleEndian(bdx.AsSpan(0x66C8, 4), 1);
        bdx[0x7F18] = 64;
        bdx[0x7F19] = 0;

        byte[] g = BdxToBbpConverter.Convert(bdx).RawGak;

        Assert.Equal((byte)0x00, g[0xFA3C]);
        Assert.Equal((byte)0xFF, g[0xFA3D]);
        Assert.Equal(new byte[] { 64, 60, 55, 0 }, g.AsSpan(0xFB2C, 4).ToArray());
    }

    [Fact]
    public void DxM6HasNoNativePQualityAndUsesExactLiteralFallback()
    {
        byte[] bdx = CreatePianoBdx(quality: 12, rawRoot: 0x00, highest: 70, voicing: 3); // C m6, 4-close

        byte[] g = BdxToBbpConverter.Convert(bdx).RawGak;

        Assert.Equal((byte)0x00, g[0xFA3C]);
        Assert.Equal((byte)0xFF, g[0xFA3D]);
        Assert.Equal(new byte[] { 69, 67, 63, 60 }, g.AsSpan(0xFB2C, 4).ToArray());
    }

    [Fact]
    public void HistoricalShortBdxTailIsZeroPaddedWithoutChangingSongData()
    {
        byte[] full = CreateMinimalBdx();
        byte[] shortDump = full[..0x7F48];

        BdxConversionResult fullResult = BdxToBbpConverter.Convert(full);
        BdxConversionResult shortResult = BdxToBbpConverter.Convert(shortDump);

        Assert.Equal(fullResult.RawGak, shortResult.RawGak);
        Assert.Single(shortResult.Warnings);
        Assert.Contains("zero-padded", shortResult.Warnings[0]);
    }

    [Fact]
    public void HarmlessBytesAfterCanonicalBdxSlotAreIgnored()
    {
        byte[] full = CreateMinimalBdx();
        byte[] overlong = [.. full, .. "</body></html>"u8.ToArray()];

        BdxConversionResult fullResult = BdxToBbpConverter.Convert(full);
        BdxConversionResult overlongResult = BdxToBbpConverter.Convert(overlong);

        Assert.Equal(fullResult.RawGak, overlongResult.RawGak);
        Assert.Single(overlongResult.Warnings);
        Assert.Contains("suffix was ignored", overlongResult.Warnings[0]);
    }

    [Fact]
    public void LegacyBbrsGakMislabelledAsBdxGetsAUsefulError()
    {
        byte[] legacy = new byte[0x6000];
        "BBRS_GAK"u8.CopyTo(legacy);

        BdxConversionException ex = Assert.Throws<BdxConversionException>(() => BdxToBbpConverter.Convert(legacy));
        Assert.Contains("legacy .bbs/GAK", ex.Message);
        Assert.Contains("not a Band Brothers DX BDX", ex.Message);
    }

    [Fact]
    public void GuitarAndPianoTogetherAreRejectedRatherThanGuessed()
    {
        byte[] bdx = CreateMinimalBdx();
        bdx[ChannelInfo + 3] = 2;
        bdx[ChannelInfo + 16 + 2] = 2;
        bdx[ChannelInfo + 16 + 3] = 3;

        Assert.Throws<BdxConversionException>(() => BdxToBbpConverter.Convert(bdx));
    }


    private static byte[] CreatePianoBdx(byte quality, byte rawRoot, byte highest, byte voicing)
    {
        byte[] bdx = CreateMinimalBdx();
        bdx[ChannelInfo + 3] = 3;
        bdx.AsSpan(0x6608, 64).Fill(0xFF);
        bdx[0x6608] = quality;
        bdx[0x6609] = rawRoot;
        bdx[0x7F18] = highest;
        bdx[0x7F19] = voicing;
        return bdx;
    }

    internal static byte[] CreateMinimalBdx()
    {
        byte[] bdx = new byte[BdxToBbpConverter.BdxSize];
        "TEST"u8.CopyTo(bdx.AsSpan(0x48));
        bdx[LinesPlus6] = 10;
        bdx[MainInstrument] = 0xFF;
        bdx[MasterVolume] = 100;

        // One ordinary populated channel, including a pan value whose mapping
        // catches the old Copilot stub (which always emitted zero).
        BinaryPrimitives.WriteInt16LittleEndian(bdx.AsSpan(ChannelInfo, 2), 100);
        bdx[ChannelInfo + 2] = 1;
        bdx[ChannelInfo + 3] = 0;
        bdx[ChannelInfo + 4] = 11;

        // Key block 0 final used state = 3. The P-only tail must carry 3, not
        // an arbitrary byte from the end of the 512-byte DX buffer.
        bdx[KeyBlocks + 15] = 3;
        return bdx;
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(12, 28)]
    [InlineData(16, 32)]
    [InlineData(100, 32)]
    public void MasterGainFollowsNintendosMigrationRule(byte dxMaster, sbyte expectedP)
    {
        byte[] bdx = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", "degausser-test-scale.bdx"));
        bdx[0x00B7] = dxMaster;
        Assert.Equal(expectedP, (sbyte)BdxToBbpConverter.Convert(bdx).RawGak[0x020A]);
        Assert.Equal((sbyte)dxMaster, (sbyte)BdxToBbpConverter.Convert(bdx,
            new BdxConversionOptions(NormaliseForP: false)).RawGak[0x020A]);
    }

    [Fact]
    public void JapaneseKaraokeIsNormalisedLikeNintendosMigration()
    {
        // あいうえお OK? / かき
        Gak gak = ConvertWithLyrics([0x91, 0x92, 0x93, 0x94, 0x95, .. " OK?"u8, 0, 0x96, 0x97]);

        // Full-width text except '?', and a terminating line break.
        Assert.Equal("あいうえお\u3000ＯＫ?\nかき\n", gak.karaokeLyrics);
        ushort[] timers = Timers(gak);
        Assert.Equal(0x8000, timers[0]);
        Assert.Equal(0xC000 | 20, timers[5]);  // full-width space: separator
        Assert.Equal(0xC000 | 36, timers[9]);  // line break: separator
        Assert.Equal(0x8000 | 40, timers[10]);
        Assert.Equal(0x7FFF, timers[^1]);       // appended line break: padding + separator
    }

    [Fact]
    public void EnglishKaraokeStaysHalfWidth()
    {
        Gak gak = ConvertWithLyrics([.. "I'm ok?"u8, 0, .. "Go!"u8]);

        Assert.Equal("I'm ok?\nGo!\n", gak.karaokeLyrics);
        ushort[] timers = Timers(gak);
        Assert.Equal(0xC000 | 12, timers[3]);  // space: separator
        Assert.Equal(0xC000 | 28, timers[7]);  // line break: separator
    }

    [Fact]
    public void PagePaddingCollapsesToOneBlankLine()
    {
        Gak gak = ConvertWithLyrics([0x91, 0, 0, 0, 0, 0x92]);

        Assert.Equal("あ\n\nい\n", gak.karaokeLyrics);
        Assert.Equal(0x8000 | 20, Timers(gak)[3]);  // い keeps its own timer
    }

    [Fact]
    public void OnlyWhitespaceKeepsTheSeparatorBitInKaraokeTimers()
    {
        // DX also flags symbols with bit 14; the official BBPs never do, and scorefix alignment needs ordinary timers.
        byte[] bdx = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", "degausser-test-scale.bdx"));
        bdx[0x00AF] = 1;
        byte[] lyrics = [0x91, 0x92, .. " "u8, 0x93];   // あい う
        lyrics.CopyTo(bdx.AsSpan(0x4E08));
        for (int i = 0; i < 1024; i++)
            BitConverter.TryWriteBytes(bdx.AsSpan(0x5608 + 2 * i), (ushort)(i < lyrics.Length ? i * 4 | 0x4000 : 0x3FFF));

        Gak gak = BdxToBbpConverter.Convert(bdx).Gak;
        ushort[] timers = Timers(gak);

        Assert.Equal(0x8000, timers[0] & 0xC000);        // a letter loses the flag
        Assert.Equal(0x8000, timers[1] & 0xC000);
        Assert.Equal(0xC000, timers[2] & 0xC000);        // the space keeps it
        Assert.Equal(0x8000, timers[3] & 0xC000);
    }

    private static Gak ConvertWithLyrics(byte[] lyrics)
    {
        byte[] bdx = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Synthetic", "degausser-test-scale.bdx"));
        bdx[0x00AF] = 1;
        lyrics.CopyTo(bdx.AsSpan(0x4E08));
        for (int i = 0; i < 1024; i++)
            BitConverter.TryWriteBytes(bdx.AsSpan(0x5608 + 2 * i), (ushort)(i < lyrics.Length ? i * 4 : 0x3FFF));
        return BdxToBbpConverter.Convert(bdx).Gak;
    }

    private static ushort[] Timers(Gak gak)
        => gak.karaokeTimer.Take(gak.karaokeLyrics.Length).Select(t => (ushort)t).ToArray();
}
