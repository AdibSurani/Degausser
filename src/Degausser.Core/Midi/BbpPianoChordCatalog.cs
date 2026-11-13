namespace Degausser;

/// <summary>
/// Native Band Brothers P Piano chord decoder recovered from the game's ELF.
/// Piano score bytes index <c>pianoPair[120]</c>; each pair is either a packed
/// native chord descriptor or a selector for one of the literal <c>pianoOrig</c>
/// voicings.
/// </summary>
internal static class BbpPianoChordCatalog
{
    private static readonly int[] RootMidi = [60, 62, 64, 65, 67, 69, 71]; // C4..B4
    private static readonly int[] AccidentalOffsets = [0, 1, -1];

    // ELF rodata 0x008DBD1C.
    private static readonly int[][] FourVoiceIntervals =
    [
        [0, 4, 7, 12],
        [0, 3, 7, 12],
        [0, 4, 7, 10],
        [0, 4, 7, 11],
        [0, 3, 7, 10],
        [0, 3, 6, 9],
        [0, 3, 6, 10],
        [0, 4, 8, 12],
        [0, 5, 7, 12],
        [0, 5, 7, 10],
        [0, 4, 7, 9],
        [0, 4, 7, 2]
    ];

    // ELF rodata 0x008DBDDC. Nintendo stores a sentinel as the fourth entry;
    // keeping only the three real voices is equivalent for the sort/rotation
    // operations that follow.
    private static readonly int[][] ThreeVoiceIntervals =
    [
        [0, 4, 7],
        [0, 3, 7],
        [0, 4, 10],
        [0, 4, 11],
        [0, 3, 10],
        [3, 6, 9],
        [3, 6, 10],
        [0, 4, 8],
        [0, 5, 7],
        [0, 5, 10],
        [4, 7, 9],
        [4, 7, 2]
    ];

    internal static bool TryResolve(
        IndexRootPair pair,
        IReadOnlyList<int> pianoOrig,
        out byte[] notes)
    {
        notes = [];
        int index = pair.index;
        int rawRoot = pair.rawRoot;
        int degree = (rawRoot >> 5) & 0x07;

        // Degree 7 is the original/custom branch. The full E0..FF family is a
        // selector; F9/FF are merely commonly observed examples.
        if (degree == 7)
        {
            if ((uint)index >= (uint)pianoOrig.Count)
                return false;

            notes = UnpackLiteralVoicing(unchecked((uint)pianoOrig[index]));
            return true;
        }

        int quality = (index >> 4) & 0x0F;
        int rotation = index & 0x0F;
        int accidental = (rawRoot >> 1) & 0x03;
        bool fourVoices = (rawRoot & 0x08) != 0;
        bool drop = (rawRoot & 0x01) != 0;

        if ((uint)degree >= (uint)RootMidi.Length
            || (uint)accidental >= (uint)AccidentalOffsets.Length
            || quality >= 12)
        {
            return false;
        }

        int root = RootMidi[degree] + AccidentalOffsets[accidental];
        int[] intervals = fourVoices ? FourVoiceIntervals[quality] : ThreeVoiceIntervals[quality];
        int[] voices = intervals.Select(interval => root + interval).Order().ToArray();

        // Native normalisation at ELF 0x001AD778 / 0x001AD8A8: keep the
        // highest real pitch at or below MIDI 70, avoiding an exact duplicate
        // of the current bass when dropping a voice.
        while (voices[^1] > 70)
        {
            int top = voices.Length - 1;
            int lowered = voices[top] - 12;
            voices[top] = lowered != voices[0] ? lowered : voices[top - 1] - 12;
            Array.Sort(voices);
        }

        // The low nibble is a voicing rotation centred on 7.
        int delta = rotation - 7;
        if (delta > 0)
        {
            for (int i = 0; i < delta; i++)
            {
                int raised = voices[0] + 12;
                voices[0] = raised != voices[^1] ? raised : voices[1] + 12;
                Array.Sort(voices);
            }
        }
        else if (delta < 0)
        {
            for (int i = 0; i > delta; i--)
            {
                int top = voices.Length - 1;
                int lowered = voices[top] - 12;
                voices[top] = lowered != voices[0] ? lowered : voices[top - 1] - 12;
                Array.Sort(voices);
            }
        }

        // rawRoot bit 0 applies the second native voicing transform: lower the
        // middle / second-highest voice by an octave, then sort again.
        if (drop)
        {
            int at = voices.Length == 4 ? 2 : 1;
            voices[at] -= 12;
            Array.Sort(voices);
        }

        // Nintendo writes generated pitches highest-to-lowest to its temporary
        // chord buffer. MIDI onset is simultaneous, but retaining that order
        // keeps converter output aligned with the native routine.
        notes = voices
            .Reverse()
            .Where(static value => value is > 0 and < 128)
            .Select(static value => (byte)value)
            .ToArray();
        return notes.Length > 0;
    }

    private static byte[] UnpackLiteralVoicing(uint encoded)
    {
        var result = new List<byte>(4);
        for (int i = 0; i < 4; i++)
        {
            byte note = unchecked((byte)(encoded >> (i * 8)));
            if (note is 0 or >= 128)
                continue;
            result.Add(note);
        }
        return result.ToArray();
    }
}
