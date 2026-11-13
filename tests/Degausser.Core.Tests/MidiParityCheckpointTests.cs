using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace Degausser.Core.Tests;

/// <summary>Direct compatibility contracts with browser BBP2MIDI 1.2.
/// Execute under .NET 10: dotnet test -c Release.</summary>
public class MidiParityCheckpointTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory,
        "TestData", "Synthetic", "degausser-test-scale.bbp");

    [Fact]
    public void BothEmbeddedProfilesResolveAll213InstrumentIds()
    {
        Assert.Equal(213, BbpMidiProfile.Mosgis.Instruments.Count);
        Assert.Equal(213, BbpMidiProfile.GeneralMidi.Instruments.Count);
        Assert.False(BbpMidiProfile.Mosgis.GmCompatible);
        Assert.True(BbpMidiProfile.GeneralMidi.GmCompatible);
        Assert.Equal("BBP.sf2", BbpMidiProfile.Mosgis.SoundFontName);
        Assert.Equal((byte)0, BbpMidiProfile.Mosgis.GetInstrument(1).MidiProgram);
        Assert.Equal((byte)1, BbpMidiProfile.GeneralMidi.GetInstrument(1).MidiProgram);
    }

    [Fact]
    public void BrowserVersionOneProfileJsonCanBeImportedWithoutDroppingInstruments()
    {
        const string json = """
        {"format":"bbp2midi-instrument-profile","version":1,"baseProfileId":"gm",
         "gmCompatible":true,"instruments":[{"id":1,"name":"My Piano","midiProgram":5,"drumNotes":[]}]}
        """;
        var profile = BbpMidiProfile.FromJson(json);
        Assert.Equal(213, profile.Instruments.Count);
        Assert.Equal((byte)5, profile.GetInstrument(1).MidiProgram);
        Assert.Equal(BbpMidiProfile.GeneralMidi.GetInstrument(42).DrumNotes,
            profile.GetInstrument(42).DrumNotes);
    }

    [Theory]
    [InlineData(-64)]
    [InlineData(0)]
    [InlineData(64)]
    public void GmAndMosgisBothWriteTheSameMasterVolumeSysEx(int rawMaster)
    {
        var record = BBPRecord.FromBBP(Fixture);
        record.gak.masterGain = checked((sbyte)rawMaster);
        byte[] gm = BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions
            { Profile = BbpMidiProfile.GeneralMidi }).File.GetTimedEvents()
            .Select(e => e.Event).OfType<NormalSysExEvent>().Single().Data;
        byte[] sf = BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions
            { Profile = BbpMidiProfile.Mosgis }).File.GetTimedEvents()
            .Select(e => e.Event).OfType<NormalSysExEvent>().Single().Data;
        Assert.Equal(gm, sf);
        Assert.Equal(new byte[] {0x7F,0x7F,0x04,0x01}, gm.Take(4).ToArray());
        Assert.Equal((byte)0xF7, gm[^1]);
        double gain = Math.Clamp(1.0 + rawMaster / 128.0, 0, 1.5);
        int expected = (int)Math.Round(Math.Sqrt(gain / 1.5) * 16383);
        Assert.Equal(expected, gm[4] | gm[5] << 7);
    }

    [Fact]
    public void GmDrumsFoldStaticAndLiveGainIntoEachHitWithoutCc7Collision()
    {
        var record = BBPRecord.FromBBP(Fixture);
        for (int source = 0; source < 2; source++)
        {
            record.gak.channelInfo[source].playType = PlayType.Drum;
            record.gak.channelInfo[source].instrument = 42;
            record.gak.channelInfo[source].volume = (byte)(source == 0 ? 127 : 32);
            Array.Clear(record.gak.channelNotes[source].notes);
            record.gak.channelNotes[source].notes[0] = 1;
            record.gak.volumeChanges[source].changes[0] =
                new TimeValuePair { time = 0, value = 127 };
            record.gak.volumeChanges[source].changes[1] =
                new TimeValuePair { time = -1, value = 0 };
        }
        MidiFile gm = BbpToMidiConverter.Convert(record, new BbpMidiConversionOptions
            { Profile = BbpMidiProfile.GeneralMidi }).File;
        var notes = gm.GetNotes().Where(n => (byte)n.Channel == 9).ToArray();
        Assert.Equal(2, notes.Length);
        Assert.Contains(notes, n => (byte)n.Velocity < 65);
        var cc7 = gm.GetTimedEvents().Select(e => e.Event)
            .OfType<ControlChangeEvent>()
            .Where(e => (byte)e.Channel == 9 && (byte)e.ControlNumber == 7)
            .ToArray();
        Assert.NotEmpty(cc7);
        Assert.All(cc7, e => Assert.Equal((byte)127, (byte)e.ControlValue));
        Assert.DoesNotContain(gm.GetTimedEvents().Select(e => e.Event).OfType<ControlChangeEvent>(),
            e => (byte)e.Channel == 9 && (byte)e.ControlNumber == 11);
    }
}
