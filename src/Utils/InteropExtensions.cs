using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Degausser
{
    static class InteropExtensions
    {
        public unsafe static T ToStruct<T>(this byte[] buffer)
        {
            fixed (byte* pBuffer = buffer)
            {
                return (T)Marshal.PtrToStructure((IntPtr)pBuffer, typeof(T));
            }
        }

        public unsafe static byte[] StructToArray<T>(this T item)
        {
            var buffer = new byte[Marshal.SizeOf(typeof(T))];
            fixed (byte* pBuffer = buffer)
            {
                Marshal.StructureToPtr(item, (IntPtr)pBuffer, false);
            }
            return buffer;
        }

        public static byte HiByte(this short s) => (byte)(s >> 8);
        public static byte LoByte(this short s) => (byte)(s & 0xFF);

        public static string ArrayToString(this char[] buffer)
        {
            return String.Concat(buffer.TakeWhile(c => c != 0));
        }

        public static char[] StringToArray(this string str, int length)
        {
            var buffer = str.ToCharArray();
            Array.Resize(ref buffer, length);
            return buffer;
        }
    }
}
