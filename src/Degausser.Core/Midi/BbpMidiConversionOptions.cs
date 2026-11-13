namespace Degausser;

/// <summary>Conversion choices. Default is BBP2MIDI 1.2 Mosgis/BBP.sf2.
/// Legacy IntensityEffect is preserved only for reading old callers; v1.2
/// intentionally uses the full recovered four-level intensity curve.</summary>
public sealed record BbpMidiConversionOptions
{
    [Obsolete("Retained for older callers; BBP2MIDI 1.2 uses native intensity without a slider.")]
    public double IntensityEffect { get; init; } = 1.0;
    public BbpMidiProfile? Profile { get; init; }
}
