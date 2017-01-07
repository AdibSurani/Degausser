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
    class SingerMgr2
    {
        public int magic;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1011)]
        public Item[] items;

        public static SingerMgr2 Empty = new SingerMgr2
        {
            magic = 0x10002,
            items = Enumerable.Repeat(Item.Empty, 1011).ToArray()
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Item
        {
            public SingerMgr.Item item;
            ulong unk1;
            ulong unk2;
            ulong unk3;
            ulong unk4;
            ulong unk5;
            ulong unk6;
            ulong unk7;
            ulong unk8;

            public static Item Empty = new Item { item = SingerMgr.Item.Empty };
        }
    }
}
