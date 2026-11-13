using Kermalis.EndianBinaryIO;

namespace Degausser;

/// <summary>
/// Shared Band Brothers guitar chord interpretation. These tables and pair
/// transforms are game-specific reverse-engineered data, not generic MIDI
/// infrastructure.
/// </summary>
internal static class BbpChordCatalog
{
    internal static readonly int[] GuitarPlayableSlots = [1, 2, 3, 4, 6, 7, 8, 9];
    internal static readonly byte[] GuitarTuning = [64, 59, 55, 50, 45, 40]; // E4 B3 G3 D3 A2 E2

    internal static readonly byte[] GuitarChords = Convert.FromHexString(
        "2000310083943206200C312000003100838C320622880021900C322030043120C3943206C38C3206A594322060003100" +
            "85C03220C4184308A4184308C4104308C4144308A4104308430C0121B090422050884120E4184308E4104308C6184320" +
            "84184308A640432062080020610800202208002042080020210800200104002121040021620C00216308002023080020" +
            "0208002060080020010800200621640CE620640C0619640C061D640CE618640C2288002142880021909062202621640C" +
            "2619640C08216420C620640CE84064200004210000002100000420000084200000002000430C0121630C012120040121" +
            "000821000008200040042100020421004000210021883102218431022188300220880121218430020104002184900121" +
            "41880121218C3102218C3002700830022388012161843102420C420442084204420C4104428C41044208410422880021" +
            "20080105620C02214210420442104104508C0005440C02218208420403002006638C52060100200602002006638C5106" +
            "430C0121508C01078390022163945206639451060000200665900221A38C520684146308841063088414620884946208" +
            "841062080104002170100209A414032184186308841862089094010986140321C4106308400801002008010040000100" +
            "4004010020000100228800209094020B418801206008010060000100420801000008010022080100618C1102418C1102" +
            "618411026188110241841102430C012150041120620C0221818C110281841102638C1120218C11024380112082102204" +
            "6210220402882020820C220462082204010400217088212083900221A2102204A2082204841022204210220402042020");

    private static readonly byte[] MajorScale = [0, 2, 4, 5, 7, 9, 11];

    internal static int ButtonForAttackCode(byte code)
    {
        int downCode = (code & 0x7F) & ~0x02;
        return downCode switch
        {
            0x05 => 0,
            0x09 => 1,
            0x0D => 2,
            0x11 => 3,
            0x19 => 4,
            0x1D => 5,
            0x21 => 6,
            0x25 => 7,
            _ => -1
        };
    }

    internal static bool IsUpStrum(byte code) => ((code & 0x7F) & 0x02) != 0;

    internal static bool TryResolveGuitarVoicing(
        IndexRootPair pair,
        IReadOnlyList<int> guitarOrig,
        out uint encoded,
        out byte[] notes)
    {
        notes = new byte[6];
        encoded = 0;
        if (pair.index == 0xFF && pair.rawRoot == 0xFF)
            return false;

        if ((pair.rawRoot & 0xE0) == 0xE0)
        {
            // The game's playback path treats every category-7 chord ID
            // (bits 15..13 == 111) as a user chord and indexes the custom
            // chord array directly with the low byte. Native-P songs have
            // been observed using E200-E21F; DX-to-P migration uses
            // EF00-EF1F. This check must precede the legacy low-nibble-F
            // decoder, otherwise E20F/E21F resolve as unrelated built-ins.
            if (pair.index >= guitarOrig.Count)
                return false;
            encoded = unchecked((uint)guitarOrig[pair.index]);
        }
        else if ((pair.index & 0x0F) != 0x0F)
        {
            // Native P built-ins are 16-bit chord IDs. The generic resolver
            // splits them into note letter (bits 15..13), accidental
            // (bits 10..9), chord quality (bits 7..4), and rotation
            // (bits 3..0), then selects the corresponding gt_chord.xml
            // voicing. Native rotations observed in the resource never use
            // low nibble F, so that value remains the migrated-DX escape path.
            ushort chordId = (ushort)((pair.rawRoot << 8) | pair.index);
            if (!BbpNativeGuitarChordCatalog.TryResolve(chordId, out encoded))
                return false;
        }
        else
        {
            // P stores a transformed DX pair:
            //   index = dxIndex * 16 + 15
            //   rawRoot = ROL8(dxRawRoot, 5)
            int chordIndex = pair.index >> 4;
            byte dxRawRoot = RotateRight8(pair.rawRoot, 5);
            int root = DecodeRoot(dxRawRoot);
            if (chordIndex >= 13 || root is < 0 or >= 12)
                return false;

            int offset = 52 * root + 4 * chordIndex;
            if (offset > GuitarChords.Length - 4)
                return false;
            encoded = EndianBinaryPrimitives.ReadUInt32(
                GuitarChords.AsSpan(offset, 4), Endianness.LittleEndian);
        }

        for (int stringIndex = 0; stringIndex < notes.Length; stringIndex++)
        {
            int fret = (int)((encoded >> (5 * stringIndex)) & 31);
            notes[stringIndex] = fret < 16
                ? checked((byte)(GuitarTuning[stringIndex] + fret))
                : (byte)0;
        }
        // A valid voicing may intentionally mark every physical string muted
        // (packed fret 16..31). Keep it resolved: playback can still emit the
        // short damped transients recovered from real-3DS listening tests.
        return true;
    }

    private static int DecodeRoot(byte rawRoot)
    {
        if (rawRoot == 0xFF) return -1;
        int degree = rawRoot & 0x0F;
        if (degree >= MajorScale.Length) return degree % 12;

        int note = MajorScale[degree];
        int accidental = rawRoot >> 4;
        if (accidental == 1) note++;
        else if (accidental == 2) note--;
        return (note + 12) % 12;
    }

    private static byte RotateRight8(byte value, int count)
    {
        count &= 7;
        if (count == 0) return value;
        return unchecked((byte)((value >> count) | (value << (8 - count))));
    }
}
