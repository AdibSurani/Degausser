using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.LayoutKind;
using static System.Runtime.InteropServices.UnmanagedType;

namespace Degausser
{
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    class BDXFormat
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 72)]
        public byte[] bdxHeader; // not required

        #region GAKHeader
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public JapWordz[] labels; // 32 bytes each
        public short definitelyZero1;
        public byte timeSignature;
        public byte linesPlus6;
        public byte definitelyZero2;
        public byte otherLineCount;
        public byte mainInstrument;
        public byte hasKaraoke;
        public int someThingy0x20080116; // 0x20080116
        public byte unknown1;
        public byte unknown2;
        public byte unknown3;
        public byte masterVolume;
        public int recordID;
        public int dateCreated;
        public int dateModified;
        public int definitelyZero3;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public ChannelInfo[] channelInfo; // 16 bytes each
        public short isOfficial;
        public short contributorLength;
        public JapWordz contributor; // 32 bytes
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 31)]
        public int[] definitelyZero4;
        /////////////////////////////////////////////////////

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct JapWordz
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            byte[] label;

            public override string ToString() => Utils.Language.GetBDXJPString(label);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ChannelInfo
        {
            public short volume;
            public byte instrument;
            public PlayType playType; // Standard, Drum, Guitar, Chord
            public byte panning;
            public byte masterProStar;
            public byte cloneID;
            public byte amaBegiStar;
            public long zero;
        }
        #endregion

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ChannelNotes
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2048)]
            public byte[] notes;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ChannelInfo5
        {
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct ChannelInfo4
            {
                public short time;
                public short value;
                public byte volume;
                public byte type;
                public byte unknown;
                public byte zero;
            }

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public ChannelInfo4[] stuff;
        }
        // guitar chord stuff
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct GuitarStuff
        {
            public short time;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
            public IndexRootPair[] pair;
        }
        // karaoke stuff
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct KeySignature
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public int[] stuff;
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public SoundEnvelope[] channelEnvelopes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public ChannelNotes[] channelNotes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public TimeValuePair[] tempoTimer;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public ChannelInfo5[] chanInfo5;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] guitarOrig;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public GuitarStuff[] guitarTimer;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2048)]
        public byte[] karaokeLyrics;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2048)]
        public short[] karaokeTimer;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public IndexRootPair[] pianoPair;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] pianoOrig;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 74)]
        public long[] totallyUnknown; // Totally unknown
        public int chordChanges; // note that you can only have piano or guitar, but not both
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
        public TimeValuePair[] chordTimer;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public KeySignature[] keySig;
        public byte pianoHighestNote;
        public byte pianoVoicingStyle;
        public byte pianoUnknown;
        public byte pianoAvailability;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 44)]
        public byte[] comments;
    }
}
