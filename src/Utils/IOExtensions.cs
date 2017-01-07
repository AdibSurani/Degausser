using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace Degausser
{
    static class IOExtensions
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

        public static string IOFriendly(this string str)
        {
            return string.Concat(str.Select(c => "<>|:*?/\\\"".Contains(c) ? (char)(c + 0xFEE0) : c));
        }

        #region GZip compression
        public static byte[] Compress(this byte[] data)
        {
            using (var compressedStream = new MemoryStream())
            {
                using (var zipStream = new GZipStream(compressedStream, CompressionMode.Compress, true))
                {
                    zipStream.Write(data, 0, data.Length);
                }
                var arr = compressedStream.ToArray();
                arr[8] = 0; // these two bytes are not strictly required, but
                arr[9] = 3; // will make it look like the original pack file
                return arr;
            }
        }

        public static byte[] Decompress(this Stream stream)
        {
            using (var zipStream = new GZipStream(stream, CompressionMode.Decompress))
            using (var resultStream = new MemoryStream())
            {
                zipStream.CopyTo(resultStream);
                return resultStream.ToArray();
            }
        }

        public static byte[] Decompress(this byte[] buffer, int index, int count) => Decompress(new MemoryStream(buffer, index, count));
        #endregion
    }
}
