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
    class MvMgr
    {
        public int magic;
        public short version;
        public short count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1000)]
        public Item[] items;

        public static MvMgr Empty = new MvMgr
        {
            magic = 0x4D564431, // "MVD1"
            version = 0x100,
            count = 1000,
            items = Enumerable.Repeat(Item.Empty, 1000).ToArray()
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Item
        {
            uint hasItem;
            uint videoID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 51)]
            public string title;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 141)]
            public string description;

            public static Item Empty = new Item();
        }
    }
}
