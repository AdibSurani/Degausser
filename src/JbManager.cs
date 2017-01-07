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
    class JbManager
    {
        const int binSize = 1155072;

        public JbMgr mgr;
        public string FilePath { get; }
        public string Directory => Path.GetDirectoryName(FilePath);

        public JbManager(string path)
        {
            FilePath = path;
            mgr = File.OpenRead(path).Decompress().ToStruct<JbMgr>();
        }

        public void Save()
        {
            var buf = mgr.StructToArray().Compress();
            using (var bw = new BinaryWriter(File.OpenWrite(FilePath)))
            {
                bw.Write(buf);
                bw.BaseStream.Position = binSize - 4;
                bw.Write(buf.Length);
            }
        }
    }
}
