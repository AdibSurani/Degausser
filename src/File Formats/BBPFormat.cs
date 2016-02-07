using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Degausser
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    class BBPFormat
    {
        public int version;
        public int titleID;
        public int dateCreated;
        public int dateModified;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 250)]
        public char[] title;
        public uint test;   //unknown
        public byte linesPlus6;
        public byte timeSignature;
        public byte masterVolumeMaybe;
        public byte mainInstrumentMaybe;

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

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public ChannelInfo[] channelInfo; // attack? decay? etc.

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ChannelNotes
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2400)]
            public byte[] notes;
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public ChannelNotes[] channelNotes;

        //[System.Diagnostics.DebuggerDisplay("({string.Join(\",\",stuff.Distinct())})")]
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SomeMask
        {
            public override string ToString() => string.Join(",", stuff.Distinct());

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 600)]
            public short[] stuff;
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public SomeMask[] unknownMask;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public TimeValuePair[] tempoChanges;

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

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public Changes1024[] volumeChanges;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public Changes512[] rangeChanges;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public Changes512[] effectorChanges;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct PanningStuff
        {
            public short zero1;
            public short value;
            public short minusOne;
            public short zero2;
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public PanningStuff[] panningMaybe;

        // 0xE2DC
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] guitarOrig;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct GuitarStuff
        {
            public short time;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public IndexRootPair[] pair;
        }

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
        public int[] pianoOrig; // need to convert the indexed bdx pianopairs to pianoorig

        // 0xFC2C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3000)]
        public char[] karaokeLyrics;

        // 0x1139C
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3000)]
        public short[] karaokeTimer; // all ORed with 0x8000

        // 0x12B0C
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 6600)]
        //public byte[] unknownStuff;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct KeySignature
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 150)]
            public int[] stuff;
        }
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
}
