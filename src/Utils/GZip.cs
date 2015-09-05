using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;

namespace Degausser.Utils
{
    static class GZip
    {
        public static byte[] Compress(byte[] data)
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

        public static byte[] Decompress(byte[] buffer, int index, int count)
        {
            using (var zipStream = new GZipStream(new MemoryStream(buffer, index, count), CompressionMode.Decompress))
            using (var resultStream = new MemoryStream())
            {
                zipStream.CopyTo(resultStream);
                return resultStream.ToArray();
            }
        }
    }
}
