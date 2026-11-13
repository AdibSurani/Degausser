namespace Degausser;

/// <summary>
/// Exact Daigasso! Band Brothers DX Piano chord resolver recovered from the
/// Japanese DX ARM9 executable (BANDBROS DX / AXBJ).
///
/// Ground-truth routines:
///   0x0204F85C  built-in chord generation
///   0x0204F704  highest-note / 3-or-4-voice / spread post-processing
///   0x0204F8F8  custom/original chord generation
///   0x0203FE2C  root descriptor decoding
///
/// The 13 x 4-byte built-in table appears verbatim at ARM9 0x021001D8.
/// </summary>
internal static class BdxPianoChordCatalog
{
    // ARM9 0x021001D8. 13 qualities, four source tones each:
    // M, m, 7, M7, m7, dim, m7b5, aug, sus4, 7sus4, 6, add9, m6.
    internal static readonly byte[] BaseChords = Convert.FromHexString(
        "3C404300" +
        "3C3F4300" +
        "3C404643" +
        "3C404743" +
        "3C3F4643" +
        "3F42453C" +
        "3F42463C" +
        "3C404400" +
        "3C414300" +
        "3C414643" +
        "4043453C" +
        "3E40433C" +
        "3F43453C");

    private static readonly int[] DiatonicRootMidi = [60, 62, 64, 65, 67, 69, 71];
    private static readonly int[] AccidentalOffsets = [0, 1, -1];

    internal static bool TryResolveBuiltIn(
        byte quality,
        byte rawRoot,
        byte highestRaw,
        byte voicingRaw,
        out byte[] notes)
    {
        notes = [];
        if (quality >= BaseChords.Length / 4 || !TryDecodeRoot(rawRoot, out int rootMidi))
            return false;

        int highest = NormalizeHighest(highestRaw);
        int mode = NormalizeVoicing(voicingRaw);

        var buffer = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            int baseNote = BaseChords[4 * quality + i];
            if (baseNote == 0)
                continue;

            int pitchClass = Mod12(baseNote + rootMidi);
            buffer[i] = unchecked((byte)WrapToHighestOctave(pitchClass, highest));
        }

        ApplyGlobalVoicing(buffer, highest, mode, preDropFourthForThreeVoices: false);
        notes = buffer;
        return true;
    }

    /// <summary>
    /// Resolve one DX custom/original chord exactly as 0x0204F8F8 does.
    /// When the per-index flag is clear, DX merely sorts the four literal
    /// pitches. When set, it applies the song-wide highest/voice/spread state.
    /// </summary>
    internal static byte[] ResolveCustom(
        ReadOnlySpan<byte> rawNotes,
        bool applyGlobalVoicing,
        byte highestRaw,
        byte voicingRaw)
    {
        if (rawNotes.Length < 4)
            throw new ArgumentException("DX custom Piano chord requires four bytes.", nameof(rawNotes));

        byte[] buffer = rawNotes[..4].ToArray();
        if (!applyGlobalVoicing)
        {
            SortDescending(buffer, 0, 4);
            return buffer;
        }

        int highest = NormalizeHighest(highestRaw);
        int mode = NormalizeVoicing(voicingRaw);
        ApplyGlobalVoicing(buffer, highest, mode, preDropFourthForThreeVoices: true);
        return buffer;
    }

    internal static int NormalizeHighest(byte raw)
    {
        int highest = raw == 0 ? 70 : raw;
        if (highest < 25) return 25;
        if (highest >= 127) return 127;
        return highest;
    }

    internal static int NormalizeVoicing(byte raw)
        => raw <= 5 ? raw : 0;

    internal static int VoiceCount(byte voicingRaw)
        => NormalizeVoicing(voicingRaw) >= 3 ? 4 : 3;

    internal static int Spread(byte voicingRaw)
        => NormalizeVoicing(voicingRaw) % 3;

    internal static bool TryDecodeRootDescriptor(byte rawRoot, out int degree, out int accidental)
    {
        degree = rawRoot & 0x0F;
        accidental = rawRoot >> 4;
        return (uint)degree < 7u && (uint)accidental < 3u;
    }

    private static bool TryDecodeRoot(byte rawRoot, out int rootMidi)
    {
        rootMidi = 0;
        if (!TryDecodeRootDescriptor(rawRoot, out int degree, out int accidental))
            return false;

        rootMidi = DiatonicRootMidi[degree] + AccidentalOffsets[accidental];
        return true;
    }

    private static void ApplyGlobalVoicing(
        byte[] buffer,
        int highest,
        int mode,
        bool preDropFourthForThreeVoices)
    {
        bool fourVoices = mode >= 3;

        // Custom resolver 0x0204F8F8 clears the fourth source byte before the
        // common post-processor when a flagged original is in a 3-voice mode.
        if (!fourVoices && preDropFourthForThreeVoices)
            buffer[3] = 0;

        // 0x0204F6AC: every nonzero source pitch is octave-wrapped into the
        // 12-note window ending at the song's highest-note setting.
        for (int i = 0; i < 4; i++)
        {
            if (buffer[i] == 0)
                continue;
            buffer[i] = unchecked((byte)WrapAbsolutePitch(buffer[i], highest));
        }

        SortDescending(buffer, 0, 4);

        if (!fourVoices)
        {
            // DX always generates/sorts all built-in source tones first, then
            // drops the lowest result for 3-voice mode. This is why e.g. C7
            // does not necessarily match P's own 3-voice C7 template.
            buffer[3] = 0;
        }
        else if (buffer[3] == 0 && buffer[0] != 0)
        {
            // Triads become four voices by duplicating the top voice an octave
            // below. With the already-descending buffer this remains lowest.
            buffer[3] = unchecked((byte)(buffer[0] - 12));
        }

        // Branch table at 0x0204F764. The exact ordering is preserved even
        // though Piano onset is simultaneous.
        switch (mode)
        {
            case 0: // 3 voices, close
            case 3: // 4 voices, close
                break;

            case 1: // 3 voices, medium
                Lower(buffer, 1, 12);
                break;

            case 2: // 3 voices, wide
                Lower(buffer, 1, 24);
                Lower(buffer, 2, 12);
                SortDescending(buffer, 1, 2);
                break;

            case 4: // 4 voices, medium
                Lower(buffer, 1, 12);
                SortDescending(buffer, 1, 3);
                break;

            case 5: // 4 voices, wide
                Lower(buffer, 1, 24);
                Lower(buffer, 3, 12);
                SortDescending(buffer, 1, 2);
                break;
        }
    }

    private static int WrapToHighestOctave(int pitchClass, int highest)
    {
        int note = pitchClass;
        while (note <= highest - 12)
            note += 12;
        while (note > highest)
            note -= 12;
        return note;
    }

    private static int WrapAbsolutePitch(int note, int highest)
    {
        while (note <= highest - 12)
            note += 12;
        while (note > highest)
            note -= 12;
        return note;
    }

    private static int Mod12(int value)
    {
        int result = value % 12;
        return result < 0 ? result + 12 : result;
    }

    private static void Lower(byte[] buffer, int index, int amount)
    {
        if (buffer[index] == 0)
            return;
        buffer[index] = unchecked((byte)(buffer[index] - amount));
    }

    private static void SortDescending(byte[] buffer, int start, int count)
    {
        Array.Sort(buffer, start, count);
        Array.Reverse(buffer, start, count);
    }
}
