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
    class JbMgr2
    {
        public int magic;
        public short version;
        public short count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1500)]
        public Item[] items;

        public static JbMgr2 Empty = new JbMgr2
        {
            magic = 0x4A4B4258, // "JKBX" // THIS IS NOT CORRECT
            version = 0x101,
            count = 1500,
            items = Enumerable.Repeat(Item.Empty, 1500).ToArray()
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        [DebuggerDisplay("{item.titleID==(uint)-1?\"(empty)\":item.title,nq}")]
        public struct Item
        {
            public JbMgr.Item item;
            public uint unk1;
            public uint unk2;

            public static Item Empty = new Item
            {
                item = JbMgr.Item.Empty
            };
        }
    }
}
