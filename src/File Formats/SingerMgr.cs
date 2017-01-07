using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Degausser
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    class SingerMgr
    {
        public int magic;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1011)]
        public Item[] items;

        public static SingerMgr Empty = new SingerMgr
        {
            magic = 0x20001,
            items = Enumerable.Repeat(Item.Empty, 1011).ToArray()
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Item
        {
            // size = 412
            uint unk1;
            uint unk2;
            short unk3;
            short unk4;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public string artist;
            uint unk5;
            uint unk6;
            uint unk7;
            uint unk8;
            uint unk9;
            uint unk10;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 180)]
            public string greeting;

            public static Item Empty = new Item();
        }
    }
}
