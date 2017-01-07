using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Degausser
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    class Gak
    {
        // 0x0
        public int version;
        public int titleID;
        public int dateCreated;
        public int dateModified;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 250)]
        public string title;
        public byte test0;   //to be renamed -- unknown, always 1?
        public byte test1;   //to be renamed -- unknown, always 0?
        public byte test2;   //to be renamed -- classic
        public sbyte test3;  //to be renamed -- main instrument
        public byte linesPlus6;
        public byte timeSignature;
        public byte masterVolumeMaybe;
        public byte mainInstrumentMaybe;

        // 0x20C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public ChannelInfo[] channelInfo; // attack? decay? etc.

        // 0x3EC
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public ChannelNotes[] channelNotes;     

        // 0x61AC
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public SomeMask[] unknownMask;

        // 0x908C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public TimeValuePair[] tempoChanges;

        // 0x928C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public Changes1024[] volumeChanges;

        // 0xBA8C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public Changes512[] rangeChanges;

        // 0xCE8C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public Changes512[] effectorChanges;    

        // 0xE28C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public PanningStuff[] panningMaybe;

        // 0xE2DC
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] guitarOrig;

        // 0xE35C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public GuitarStuff[] guitarTimer;

        // 0xE77C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 600)]
        public long[] unknownGuitar3;

        // 0xFA3C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 120)]
        public IndexRootPair[] pianoPair;

        // 0xFB2C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public int[] pianoOrig; // convert indexed bdx pianopairs to pianoorig?

        // 0xFC2C
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 3000)]
        public string karaokeLyrics;

        // 0x1139C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3000)]
        public short[] karaokeTimer;

        // 0x12B0C   
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public KeySignature[] keySig;

        // 0x144D4
        public int pianoChordChangesCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 300)]
        public TimeValuePair[] pianoChordChangeTable;

        // 0x14988
        public int guitarChordChangesCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 300)]
        public TimeValuePair[] guitarChordChangeTable;
    }

    #region Struct definitions
    public enum PlayType : byte { Standard, Drum, Guitar, Piano };

    [DebuggerDisplay("({attack}, {decay}, {sustain}, {release})")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SoundEnvelope
    {
        public byte attack;
        public byte decay;
        public byte sustain;
        public byte release;
        public byte shape;
        public byte hold;
        public byte delay;
        public byte depth;
        public byte speed;
        public byte zero;
        public byte effectorType;
        public byte effectorValue;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ChannelInfo
    {
        public PlayType playType;
        public byte instrument;
        public byte volume;
        public byte cloneID;
        public byte masterProStar;
        public byte amaBegiStar;
        public byte zero;
        public byte eight;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] otherInformation; // master pro star etc.
        public SoundEnvelope env1;
        public int unknown1;
        public SoundEnvelope env2;
        public int unknown2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ChannelNotes
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2400)]
        public byte[] notes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SomeMask
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 600)]
        public short[] stuff;
    }

    [DebuggerDisplay("({time}, {value})")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TimeValuePair
    {
        public short time;
        public short value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Changes512
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public TimeValuePair[] changes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Changes1024
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public TimeValuePair[] changes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PanningStuff
    {
        public short zero1;
        public short value;
        public short minusOne;
        public short zero2;
    }

    [DebuggerDisplay("({index}, {rawRoot})")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct IndexRootPair
    {
        public byte index;
        public byte rawRoot;

        enum Accidental { Natural, Sharp, Flat };

        public byte Root
        {
            get
            {
                if (rawRoot == 0xFF) { return rawRoot; }
                byte note = InstrumentData.MajorScale[rawRoot & 0xF];
                switch ((Accidental)(rawRoot >> 4))
                {
                    case Accidental.Sharp:
                        return ++note;
                    case Accidental.Flat:
                        return --note;
                    case Accidental.Natural:
                    default:
                        return note;
                }
            }
        }

        public short Short => (short)((rawRoot << 8) | index);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GuitarStuff
    {
        public short time;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public IndexRootPair[] pair;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct KeySignature
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 150)]
        public int[] stuff;
    }
    #endregion
}
