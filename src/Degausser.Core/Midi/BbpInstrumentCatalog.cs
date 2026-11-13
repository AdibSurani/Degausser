using System.Xml.Linq;

namespace Degausser;

public sealed record BbpInstrumentInfo(
    int Id,
    string Name,
    byte? MidiProgram,
    IReadOnlyList<byte> DrumNotes,
    int? SoundFontBank = null,
    byte? SoundFontProgram = null)
{
    public bool IsDrum => DrumNotes.Count > 0;
}

/// <summary>
/// Band Brothers instrument metadata recovered by the original Degausser.
/// The XML remains the source of truth for playback mappings, including the
/// historical GT/KB ranges, migrated-DX entries and BBP.sf2-specific bank/program overrides.
/// </summary>
public static class BbpInstrumentCatalog
{
    private static readonly IReadOnlyDictionary<int, BbpInstrumentInfo> Instruments = Load();

    public static BbpInstrumentInfo Get(int id)
        => Instruments.TryGetValue(id, out var instrument)
            ? instrument
            : new BbpInstrumentInfo(id, $"Unknown instrument {id}", null, Array.Empty<byte>());

    private static IReadOnlyDictionary<int, BbpInstrumentInfo> Load()
    {
        var assembly = typeof(BbpInstrumentCatalog).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(".Midi.Instruments.xml", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Could not load embedded Band Brothers instrument catalogue.");

        var root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("Instrument catalogue has no root element.");
        var result = new Dictionary<int, BbpInstrumentInfo>();

        // First load concrete instruments. Older catalogues represented GT_DX
        // entries as ReplaceWith aliases; the recovered current catalogue lists
        // 197..212 explicitly as SCL entries.
        foreach (var x in root.Elements("Instrument"))
        {
            int id = RequiredInt(x, "ID");
            string type = Required(x, "Type");
            if (type == "GT_DX")
                continue;

            string name = Required(x, "Name");
            string midi = Required(x, "Midi");
            result[id] = type switch
            {
                "NUL" or "SCL" => new BbpInstrumentInfo(
                    id, name, byte.Parse(midi), Array.Empty<byte>(),
                    OptionalByte(x, "BbpSfBank"), OptionalByte(x, "BbpSfProgram")),
                "DRM" => new BbpInstrumentInfo(id, name, null, midi.Split(',').Select(byte.Parse).ToArray()),
                _ => throw new InvalidDataException($"Unknown instrument type '{type}' for ID {id}.")
            };
        }

        // Historical GT / KB play-type ranges reuse the corresponding base
        // timbre but retain their own BBP instrument ID.
        foreach (var x in root.Elements("InstrumentRange"))
        {
            int[] range = Required(x, "ID").Split('-').Select(int.Parse).ToArray();
            if (range.Length != 2)
                throw new InvalidDataException("InstrumentRange ID must be start-end.");
            int offset = RequiredInt(x, "Offset");
            for (int id = range[0]; id <= range[1]; id++)
            {
                int sourceId = id + offset;
                if (!result.TryGetValue(sourceId, out var source))
                    throw new InvalidDataException($"InstrumentRange {range[0]}-{range[1]} refers to missing ID {sourceId}.");
                result[id] = source with { Id = id };
            }
        }

        // Keep compatibility with older catalogues where P's GT_DX IDs
        // 197..212 were represented as ReplaceWith aliases.
        foreach (var x in root.Elements("Instrument").Where(x => Required(x, "Type") == "GT_DX"))
        {
            int id = RequiredInt(x, "ID");
            int sourceId = RequiredInt(x, "ReplaceWith");
            if (!result.TryGetValue(sourceId, out var source))
                throw new InvalidDataException($"GT_DX instrument {id} ReplaceWith refers to missing ID {sourceId}.");

            string name = x.Attribute("Name")?.Value ?? $"{source.Name} (DX)";
            result[id] = source with { Id = id, Name = name };
        }

        return result;
    }

    private static string Required(XElement element, string name)
        => element.Attribute(name)?.Value
           ?? throw new InvalidDataException($"{element.Name} is missing required {name} attribute.");

    private static int RequiredInt(XElement element, string name) => int.Parse(Required(element, name));

    private static byte? OptionalByte(XElement element, string name)
        => byte.TryParse(element.Attribute(name)?.Value, out byte value) ? value : null;
}
