using System.Text.Json;

namespace Degausser;

/// <summary>
/// Loads the same bbp2midi-instrument-profile JSON format as the browser.
/// Both v1 and v2 are accepted. Unspecified instrument fields inherit the
/// embedded base profile. Unrecognised fields survive on disk (we never rewrite
/// imported profiles), so legacy custom JSON does not have to be migrated.
/// </summary>
public sealed class BbpMidiProfile
{
    public sealed record Instrument(int Id, string Name, byte? MidiProgram,
        IReadOnlyList<byte> DrumNotes, int? SfBank, byte? SfProgram, int Transpose);

    public string Name { get; private init; } = string.Empty;
    public string BaseProfileId { get; private init; } = "mosgis";
    public string SoundFontName { get; private init; } = "BBP.sf2";
    public string SoundFontUrl { get; private init; } = string.Empty;
    public bool GmCompatible { get; private init; }
    public string TextEncoding { get; private init; } = "utf-8";
    public IReadOnlyDictionary<int, Instrument> Instruments { get; private init; } =
        new Dictionary<int, Instrument>();

    private static readonly Lazy<BbpMidiProfile> MosgisLazy =
        new(() => LoadEmbedded("mosgis-bbp-sf2.json"));
    private static readonly Lazy<BbpMidiProfile> GmLazy =
        new(() => LoadEmbedded("gm-compatible.json"));

    public static BbpMidiProfile Mosgis => MosgisLazy.Value;
    public static BbpMidiProfile GeneralMidi => GmLazy.Value;

    public Instrument GetInstrument(int id)
        => Instruments.TryGetValue(id, out var entry)
            ? entry
            : new Instrument(id, $"Unknown instrument {id}", null, [], null, null, 0);

    public static BbpMidiProfile FromFile(string path)
        => FromJson(File.ReadAllText(path));

    private static BbpMidiProfile LoadEmbedded(string file)
    {
        var assembly = typeof(BbpMidiProfile).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".Midi.Profiles." + file, StringComparison.Ordinal));
        using Stream input = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded MIDI profile {file}");
        using var reader = new StreamReader(input);
        return FromJson(reader.ReadToEnd());
    }

    public static BbpMidiProfile FromJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (String(root, "format", "bbp2midi-instrument-profile") != "bbp2midi-instrument-profile")
            throw new InvalidDataException("Not a BBP2MIDI instrument profile JSON.");
        int version = Int(root, "version", 1);
        if (version is < 1 or > 2)
            throw new InvalidDataException($"Unsupported BBP2MIDI profile version {version}.");
        string baseId = String(root, "baseProfileId", "mosgis");
        bool gm = Bool(root, "gmCompatible", baseId.Equals("gm", StringComparison.OrdinalIgnoreCase));
        // Embedded v2 profiles are complete; custom v1/v2 can be sparse.
        // Resolve against the requested built-in preset, unless we are actually
        // loading that embedded preset (which would recurse).
        bool complete = root.TryGetProperty("instruments", out JsonElement supplied)
            && supplied.ValueKind == JsonValueKind.Array && supplied.GetArrayLength() == 213;
        BbpMidiProfile? baseline = complete ? null : gm ? GeneralMidi : Mosgis;
        var entries = baseline is null ? new Dictionary<int, Instrument>()
            : new Dictionary<int, Instrument>(baseline.Instruments);
        if (root.TryGetProperty("instruments", out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                int id = Int(item, "id", -1);
                if (id is < 0 or > 212) throw new InvalidDataException($"Invalid BBP instrument ID {id}.");
                entries.TryGetValue(id, out Instrument? previous);
                byte? midi = NullableByte(item, "midiProgram", previous?.MidiProgram);
                byte? sfProgram = NullableByte(item, "bbpSfProgram", previous?.SfProgram);
                int? sfBank = NullableInt(item, "bbpSfBank", previous?.SfBank);
                int transpose = Int(item, "transpose", previous?.Transpose ?? 0);
                if (transpose is < -48 or > 48) throw new InvalidDataException($"Instrument {id} transpose out of range.");
                IReadOnlyList<byte> drums = previous?.DrumNotes ?? [];
                if (item.TryGetProperty("drumNotes", out JsonElement drumElement) && drumElement.ValueKind == JsonValueKind.Array)
                    drums = drumElement.EnumerateArray().Select(n => checked((byte)n.GetInt32())).ToArray();
                entries[id] = new Instrument(id, String(item, "name", previous?.Name ?? $"Instrument {id}"),
                    midi, drums, sfBank, sfProgram, transpose);
            }
        }
        if (entries.Count != 213)
            throw new InvalidDataException($"BBP2MIDI profile must resolve all 213 instruments; found {entries.Count}.");
        return new BbpMidiProfile
        {
            Name = String(root, "name", baseline?.Name ?? "Custom"),
            BaseProfileId = baseId,
            SoundFontName = String(root, "soundFontName", baseline?.SoundFontName ?? "BBP.sf2"),
            SoundFontUrl = String(root, "soundFontUrl", baseline?.SoundFontUrl ?? string.Empty),
            GmCompatible = gm,
            TextEncoding = String(root, "textEncoding", baseline?.TextEncoding ?? "utf-8"),
            Instruments = entries
        };
    }

    private static string String(JsonElement parent, string name, string fallback)
        => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;
    private static int Int(JsonElement parent, string name, int fallback)
        => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : fallback;
    private static bool Bool(JsonElement parent, string name, bool fallback)
        => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : fallback;
    private static int? NullableInt(JsonElement parent, string name, int? fallback)
        => parent.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.Null ? null : value.GetInt32()
            : fallback;
    private static byte? NullableByte(JsonElement parent, string name, byte? fallback)
    {
        int? raw = NullableInt(parent, name, fallback);
        return raw is null ? null : checked((byte)raw.Value);
    }
}
