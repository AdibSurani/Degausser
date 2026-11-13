using Melanchall.DryWetMidi.Core;

namespace Degausser;

public sealed record BbpMidiChannelInfo(
    int SourceChannel,
    byte PlaybackChannel,
    byte CanonicalChannel,
    int InstrumentId,
    string InstrumentName,
    PlayType PlayType,
    bool IsDrum,
    bool IsUnused,
    int? SoundFontBank,
    byte? SoundFontProgram);

/// <summary>
/// UI-neutral MIDI projection of one BBP. File and PlaybackFile are the same
/// MIDI representation in this parity checkpoint, like BBP2MIDI 1.2.
/// The desktop player renders each MIDI port with its own synthesizer.
/// </summary>
public sealed class BbpMidiDocument
{
    internal BbpMidiDocument(
        MidiFile file,
        MidiFile playbackFile,
        IReadOnlyList<BbpMidiChannelInfo> channels,
        int lengthFrames,
        int ticksPerFrame,
        sbyte masterGainRaw)
    {
        File = file;
        PlaybackFile = playbackFile;
        Channels = channels;
        LengthFrames = lengthFrames;
        TicksPerFrame = ticksPerFrame;
        MasterGainRaw = masterGainRaw;
    }

    public MidiFile File { get; }
    public MidiFile PlaybackFile { get; }
    public IReadOnlyList<BbpMidiChannelInfo> Channels { get; }

    /// <summary>Length in the game's original 12-frames-per-quarter timeline.</summary>
    public int LengthFrames { get; }

    /// <summary>MIDI resolution used for one Band Brothers frame.</summary>
    public int TicksPerFrame { get; }

    /// <summary>
    /// Signed packed GAK +0x20A ALL/master value. The real 3DS editor has been
    /// verified to display this as 1 + raw/128 (for example +43 -> 1.33x).
    /// </summary>
    public sbyte MasterGainRaw { get; }

    /// <summary>
    /// Multiplier shown by the real 3DS ALL/master UI: 1 + raw/128.
    /// This is hardware-verified as a display/control mapping; it is not yet
    /// proven to be the final NintendoWare DSP amplitude transfer function.
    /// </summary>
    public double MasterDisplayMultiplier => (128 + MasterGainRaw) / 128.0;

    /// <summary>
    /// Conservative playback approximation used until the native DSP master
    /// transfer function is recovered. For now this deliberately follows the
    /// hardware-verified visible multiplier rather than the unproven ~raw/112
    /// curve suggested by external amplitude measurements.
    /// </summary>
    public double MasterPlaybackGainApproximation => MasterDisplayMultiplier;

    /// <summary>
    /// Backward-compatible alias for the playback approximation. Prefer
    /// <see cref="MasterDisplayMultiplier"/> or
    /// <see cref="MasterPlaybackGainApproximation"/> in new code.
    /// </summary>
    public double MasterGain => MasterPlaybackGainApproximation;

    public long LengthTicks => (long)LengthFrames * TicksPerFrame;
}
