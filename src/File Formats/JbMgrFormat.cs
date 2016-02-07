using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Degausser
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    class JbMgrFormat
    {
        public int Magic;
        public short Version;
        public short Count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3700)]
        public JbMgrItem[] Items;

        public int BinSize => 1155072;

        public static JbMgrFormat Empty = new JbMgrFormat
        {
            Magic = 0x4A4B4258,
            Version = 0x101,
            Count = 3700,
            Items = Enumerable.Repeat(JbMgrItem.Empty, 3700).ToArray()
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        [DebuggerDisplay("{ID==-1?\"(empty)\":Title,nq}")]
        public struct JbMgrItem
        {
            [DebuggerDisplay("0x{ID.ToString(\"x8\")}")]
            public uint ID;
            public uint OtherID;
            public JbFlags Flags;
            public JbSinger Singer;
            public JbIcon Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 51)]
            public string Title;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 51)]
            public string TitleSimple;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string Author;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
            public byte[] Scores; // Beginner, Amateur, Pro, Master, Vocals
            public short ZeroPadding;

            public static JbMgrItem Empty = new JbMgrItem
            {
                ID = 0xFFFFFFFF,
                Singer = JbSinger.None,
                Title = "",
                TitleSimple = "",
                Author = "",
                Scores = new byte[50]
            };

            public enum JbSinger : short
            {
                None = -1, OriginalMale = -2, OriginalFemale = -3//, Player0 = 0
            }

            public enum JbIcon : short
            {
                None, Heart, Skull, Music, Warning
            }

            [DebuggerDisplay("{IsValid}")]
            public struct JbFlags
            {
                public int flag;

                // Bit0 = 1
                public bool OnSD { get { return this[1]; } set { this[1] = value; } } // best guess
                // Bit2 = 0
                public int Parts { get { return (flag >> 3) % 16; } set { flag = (flag & ~120) | (value << 3); } } // Bit3-7
                public bool HasMelody { get { return this[8]; } set { this[8] = value; } }
                public bool IsSingable { get { return this[9]; } set { this[9] = value; } } // In the UTAU section
                public bool IsClassic { get { return this[10]; } set { this[10] = value; } } //classic? foreign?
                public bool HasLyrics { get { return this[11]; } set { this[11] = value; } }
                public bool IsReceived { get { return this[12]; } set { this[12] = value; } }
                public bool HasFans { get { return this[13]; } set { this[13] = value; } }
                public bool HasGuitar { get { return this[14]; } set { this[14] = value; } }
                public bool HasDrum { get { return this[15]; } set { this[15] = value; } }
                public bool HasPiano { get { return this[16]; } set { this[16] = value; } }
                public bool HasInstrX { get { return this[17]; } set { this[17] = value; } }
                // Bit18-19 = 0, something to do with DL?
                public bool HasVocals { get { return this[20]; } set { this[20] = value; } } // should be same as IsSingEnabled
                public bool IsOnlineable { get { return this[21]; } set { this[21] = value; } } // best guess
                // Bit22 = 0
                public bool IsHidden { get { return this[23]; } set { this[23] = value; } }

                // Helper functions
                public bool this[int i]
                {
                    get { return (flag >> i) % 2 == 1; }
                    set { flag = (flag & ~(1 << i)) | ((value ? 1 : 0) << i); }
                }
                public bool IsValid
                {
                    // Check bit 0 is set and bits 2,10,17-19,22 are unset:
                    get { return (flag & 0x4C0005) == 1 && IsSingable == HasVocals; }
                    set { flag = (flag & ~0x4C0005) | (value ? 1 : 0); }
                }
            }
        }
    }
}
