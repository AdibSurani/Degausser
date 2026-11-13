namespace Degausser.Core.Tests;

public class GakLayoutTests
{
    [Fact]
    public void SolvedHeaderAndChannelInfoFieldsUseRecoveredOffsets()
    {
        byte[] raw = new byte[Gak.BinarySize];
        raw[0x20A] = 0xC0; // signed -64

        const int channel = 0x20C;
        raw[channel + 0x06] = 2;
        raw[channel + 0x07] = 30;
        raw[channel + 0x08] = 20;
        raw[channel + 0x09] = 0x11;
        raw[channel + 0x0A] = 0x13;
        raw[channel + 0x0B] = 0x5A; // non-canonical marker: must still round-trip
        raw[channel + 0x0C] = 0x11;
        raw[channel + 0x0D] = 0x22;
        raw[channel + 0x0E] = 0x33;
        raw[channel + 0x0F] = 0x44;

        // Tone A recovered byte order: speed, depth, target, dual, dual-rate,
        // biquad type/value, then signed i16 LPF.
        raw[channel + 0x10 + 0x07] = 0x27;
        raw[channel + 0x10 + 0x08] = 0x1E;
        raw[channel + 0x10 + 0x09] = 0x02;
        raw[channel + 0x10 + 0x0A] = 0x03;
        raw[channel + 0x10 + 0x0B] = 0x0A;
        raw[channel + 0x10 + 0x0C] = 0x01;
        raw[channel + 0x10 + 0x0D] = 0x19;
        raw[channel + 0x10 + 0x0E] = 0xFE;
        raw[channel + 0x10 + 0x0F] = 0xFF; // -2

        Gak gak = Gak.Read(raw);
        ChannelInfo info = gak.channelInfo[0];

        Assert.Equal((sbyte)-64, gak.masterGain);
        Assert.Equal((byte)2, info.noteStrengthCurve);
        Assert.Equal((byte)30, info.performanceParam07);
        Assert.Equal((byte)20, info.performanceParam08);
        Assert.Equal((byte)0x11, info.difficultyMetadata09);
        Assert.Equal((byte)0x13, info.vocalDifficultyMetadata);
        Assert.Equal((byte)3, info.VocalStars);
        Assert.Equal((byte)0x5A, info.reserved0B);
        Assert.Equal(0x44332211u, info.reserved0C0F);
        Assert.Equal((byte)0x27, info.toneA.speed);
        Assert.Equal((byte)0x1E, info.toneA.depth);
        Assert.Equal((byte)0x02, info.toneA.target);
        Assert.Equal((byte)0x03, info.toneA.dual);
        Assert.Equal((byte)0x0A, info.toneA.dualRate);
        Assert.Equal((byte)0x01, info.toneA.biquadType);
        Assert.Equal((byte)0x19, info.toneA.biquadValue);
        Assert.Equal((short)-2, info.toneA.lpfValue);

        Assert.Equal(raw, gak.ToBytes());
    }

    [Fact]
    public void PackedNoteStrengthsUseStoredByteOrderHighThenLow()
    {
        var packed = new PackedNoteStrengths { words = [0x1234] };

        // 0x1234 is stored little-endian as bytes 34 12. Native playback
        // consumes each byte high nibble then low nibble: 3, 4, 1, 2.
        Assert.Equal((byte)3, packed.GetNibble(0));
        Assert.Equal((byte)4, packed.GetNibble(1));
        Assert.Equal((byte)1, packed.GetNibble(2));
        Assert.Equal((byte)2, packed.GetNibble(3));
        Assert.Equal((byte)0x0F, packed.GetNibble(2400));
    }
    [Fact]
    public void KeyStateRegionIsExposedAsElevenSixHundredByteTimelines()
    {
        byte[] raw = new byte[Gak.BinarySize];
        raw[0x12B0C + 0] = 0x80;
        raw[0x12B0C + 599] = 0x12;
        raw[0x12B0C + 600] = 0x34;
        raw[0x12B0C + 10 * 600 + 42] = 0x56;

        Gak gak = Gak.Read(raw);

        Assert.Equal(11, gak.keyState.Length);
        Assert.All(gak.keyState, timeline => Assert.Equal(600, timeline.state.Length));
        Assert.Equal((byte)0x80, gak.keyState[0].state[0]);
        Assert.Equal((byte)0x12, gak.keyState[0].state[599]);
        Assert.Equal((byte)0x34, gak.keyState[1].state[0]);
        Assert.Equal((byte)0x56, gak.keyState[10].state[42]);
        Assert.Equal(raw, gak.ToBytes());
    }

    [Fact]
    public void TonePresetTimelineAndPanKeepTheirRecoveredStructures()
    {
        byte[] raw = new byte[Gak.BinarySize];
        // Tone preset A at time 0, then terminator.
        raw[0xCE8C + 0] = 0;
        raw[0xCE8C + 1] = 0;
        raw[0xCE8C + 2] = 0;
        raw[0xCE8C + 3] = 0;
        raw[0xCE8C + 4] = 0xFF;
        raw[0xCE8C + 5] = 0xFF;
        // Pan tuple (0, -47, -1, 0).
        raw[0xE28C + 2] = unchecked((byte)-47);
        raw[0xE28C + 3] = 0xFF;
        raw[0xE28C + 4] = 0xFF;
        raw[0xE28C + 5] = 0xFF;

        Gak gak = Gak.Read(raw);

        Assert.Equal((short)0, gak.tonePresetChanges[0].changes[0].time);
        Assert.Equal((short)0, gak.tonePresetChanges[0].changes[0].value);
        Assert.Equal((short)-1, gak.tonePresetChanges[0].changes[1].time);
        Assert.Equal((short)-47, gak.pan[0].value);
        Assert.Equal((short)-1, gak.pan[0].minusOne);
        Assert.Equal(raw, gak.ToBytes());
    }

}
