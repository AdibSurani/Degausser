namespace Degausser.Core.Tests;

public class KaraokeTests
{
    [Fact]
    public void SentinelTimerDisablesKaraokeAndPositionUpdatesAreSafe()
    {
        var gak = new Gak
        {
            karaokeLyrics = "unused",
            linesPlus6 = 8
        };
        gak.karaokeTimer[0] = 0x3FFF;

        var karaoke = new Karaoke(gak);
        karaoke.Position = 24;

        Assert.False(karaoke.IsEnabled);
        Assert.Equal("(no karaoke)", karaoke.Lyrics);
        Assert.Equal(0, karaoke.PositionStart);
        Assert.Equal(0, karaoke.PositionCount);
        Assert.Equal(0, karaoke.PositionFraction);
    }

    [Fact]
    public void EmptyLyricsDisableKaraokeEvenWithAnActiveTimer()
    {
        var gak = new Gak { karaokeLyrics = string.Empty };
        gak.karaokeTimer[0] = unchecked((short)0x8000);

        Assert.False(gak.HasKaraoke);
        Assert.False(new Karaoke(gak).IsEnabled);
    }

    [Fact]
    public void PositionGroupsCharactersWithTheSameTimerAndInterpolatesProgress()
    {
        var gak = new Gak
        {
            karaokeLyrics = "hello",
            linesPlus6 = 8 // two lines => 96 score ticks
        };
        Array.Fill(gak.karaokeTimer, (short)0x3FFF);
        gak.karaokeTimer[0] = unchecked((short)0x8000);
        gak.karaokeTimer[1] = unchecked((short)0x8000);
        gak.karaokeTimer[2] = unchecked((short)0x8000);
        gak.karaokeTimer[3] = unchecked((short)0x800C);
        gak.karaokeTimer[4] = unchecked((short)0x800C);

        var karaoke = new Karaoke(gak);
        karaoke.Position = 6;

        Assert.True(karaoke.IsEnabled);
        Assert.Equal("hello", karaoke.Lyrics);
        Assert.Equal(0, karaoke.PositionStart);
        Assert.Equal(3, karaoke.PositionCount);
        Assert.Equal(0.5, karaoke.PositionFraction, precision: 6);

        karaoke.Position = 12;
        Assert.Equal(3, karaoke.PositionStart);
        Assert.Equal(2, karaoke.PositionCount);
        Assert.Equal(0, karaoke.PositionFraction);

        // The original game logic caps a karaoke syllable/highlight at 48
        // ticks even when no later syllable exists.
        karaoke.Position = 60;
        Assert.Equal(5, karaoke.PositionStart);
        Assert.Equal(0, karaoke.PositionCount);
        Assert.Equal(0, karaoke.PositionFraction);
    }

    [Fact]
    public void RetimeInterpolatesCharactersAndPreservesWhitespaceFlag()
    {
        var source = new[]
        {
            Tuple.Create("la ", (short)0),
            Tuple.Create("di", (short)24)
        };

        short[] timer = Karaoke.Retime(source);

        Assert.Equal(3000, timer.Length);
        Assert.Equal(unchecked((short)0x8000), timer[0]);
        Assert.Equal(unchecked((short)0x800C), timer[1]);
        Assert.Equal(unchecked((short)0xC00C), timer[2]);
        Assert.Equal(unchecked((short)0x8018), timer[3]);
        Assert.Equal(unchecked((short)0x8018), timer[4]);
        Assert.All(timer.Skip(5), value => Assert.Equal((short)0x3FFF, value));
    }

    [Fact]
    public void KaraokeTextAndTimerSurviveGakBinaryRoundTrip()
    {
        var gak = new Gak
        {
            karaokeLyrics = "la la",
            linesPlus6 = 10
        };
        Array.Fill(gak.karaokeTimer, (short)0x3FFF);
        gak.karaokeTimer[0] = unchecked((short)0x8000);
        gak.karaokeTimer[1] = unchecked((short)0x8006);
        gak.karaokeTimer[2] = unchecked((short)0xC00C);
        gak.karaokeTimer[3] = unchecked((short)0x8012);
        gak.karaokeTimer[4] = unchecked((short)0x8018);

        byte[] bytes = gak.ToBytes();
        Gak reread = Gak.Read(bytes);

        Assert.Equal("la la", reread.karaokeLyrics);
        Assert.Equal(gak.karaokeTimer, reread.karaokeTimer);
        Assert.Equal(bytes, reread.ToBytes());
    }
    [Fact]
    public void SourceAwareRetimeIgnoresWhitespaceThatCarriesAnActiveFlag()
    {
        string source = "も\nも";
        short[] original = Enumerable.Repeat((short)0x3FFF, 3000).ToArray();
        original[0] = unchecked((short)0x8000);
        original[1] = unchecked((short)0x8006); // native whitespace anchor: must not consume a syllable
        original[2] = unchecked((short)0x800C);

        var result = Karaoke.RetimeLyrics(source, "momo", original);

        Assert.Equal("momo", result.Lyrics);
        Assert.Equal(unchecked((short)0x8000), result.Timer[0]);
        Assert.Equal(unchecked((short)0x8006), result.Timer[1]);
        Assert.Equal(unchecked((short)0x800C), result.Timer[2]);
        Assert.Equal(unchecked((short)0x800C), result.Timer[3]);
    }

    [Fact]
    public void RetimeRejectsPhoneticallyUnrelatedTargetText()
    {
        short[] original = Enumerable.Repeat((short)0x3FFF, 3000).ToArray();
        original[0] = unchecked((short)0x8000);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Karaoke.Retime("も", "q", original));

        Assert.Contains("Mismatch", error.Message);
    }

    [Fact]
    public void RetimeRejectsExtraUnmatchedTargetText()
    {
        short[] original = Enumerable.Repeat((short)0x3FFF, 3000).ToArray();
        original[0] = unchecked((short)0x8000);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Karaoke.Retime("も", "moq", original));

        Assert.Contains("extra unmatched text", error.Message);
    }

    [Fact]
    public void ParenthesisedOverridesAreAlignmentMarkupAndAreNotDisplayed()
    {
        string source = "２５じ";
        short[] original = Enumerable.Repeat((short)0x3FFF, 3000).ToArray();
        original[0] = unchecked((short)0x8010);
        original[1] = unchecked((short)0x801C);
        original[2] = unchecked((short)0x8025);

        var result = Karaoke.RetimeLyrics(source, "(nijuu)(go)ji", original);

        Assert.Equal("nijuugoji", result.Lyrics);
        Assert.Equal(0x10, result.Timer[0] & 0x3FFF);
        Assert.Equal(0x1C, result.Timer[5] & 0x3FFF);
        Assert.Equal(0x25, result.Timer[7] & 0x3FFF);
        Assert.DoesNotContain('(', result.Lyrics);
    }

    [Fact]
    public void SourceAwareRetimeKeepsFortyEightTickInterpolationCap()
    {
        string source = "かき";
        short[] original = Enumerable.Repeat((short)0x3FFF, 3000).ToArray();
        original[0] = unchecked((short)0x8000);
        original[1] = unchecked((short)0x8060); // 96 ticks later

        var result = Karaoke.RetimeLyrics(source, "kaki", original);

        Assert.Equal(0, result.Timer[0] & 0x3FFF);
        Assert.Equal(24, result.Timer[1] & 0x3FFF); // min(96,48)/2
        Assert.Equal(96, result.Timer[2] & 0x3FFF);
    }


    [Fact]
    public void KatakanaParticleHaMayBeRomanisedAsWa()
        => Assert.Equal(["ki", "se", "tsu", "wa"], "キセツハ".Romanize("kisetsu wa").Select(x => x.Trim()).ToList());

    [Fact]
    public void JapanesePunctuationWithoutARomajiCounterpartIsSkipped()
        => Assert.Equal(["", "", "ka", "na", "", ""], "’’かな’’".Romanize("kana").ToList());

    [Fact]
    public void RomanizeIgnoresTheCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.Equal(["I", "chi"], "いち".Romanize("Ichi").Select(x => x.Trim()).ToList());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void KanaStillMustMatch()
        => Assert.Throws<InvalidDataException>(() => "かな".Romanize("kami"));

    [Fact]
    public void WaveDashLengthensAVowelLikeTheChoonpu()
        => Assert.Equal(["shi", "tsu", "ko", "o", "i"], "しつこ～い".Romanize("shitsukooi").ToList());

    [Fact]
    public void ApostropheAfterSyllabicNStaysWithTheN()
    {
        Assert.Equal(["ko", "n'", "ya"], "こんや".Romanize("kon'ya").ToList());
        Assert.Equal(["KO", "N’", "YA"], "コンヤ".Romanize("KON’YA").ToList());
        Assert.Throws<InvalidDataException>(() => "かな".Romanize("k'ana"));
        Assert.Throws<InvalidDataException>(() => "かな".Romanize("kan'a"));
    }
}
