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

        public JbMgrFormat mgr;
        public string FilePath { get; }
        public string Directory => Path.GetDirectoryName(FilePath);

        public JbManager(string path)
        {
            FilePath = path;

            if (new FileInfo(path).Length != binSize)
            {
                throw new InvalidDataException($"{path} has an incorrect filesize!");
            }

            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
                {
                    gz.CopyTo(ms);
                }

                // Convert file contents into our struct
                if (ms.Length != Marshal.SizeOf<JbMgrFormat>())
                {
                    throw new InvalidDataException($"{path} has an incorrect buffer length!");
                }
                mgr = ms.ToArray().ToStruct<JbMgrFormat>();
            }
        }

        public void Save()
        {
            SaveToFile(FilePath);
            SaveToFile(Path.Combine(Path.GetDirectoryName(FilePath), "mgr_.bin"));
        }

        void SaveToFile(string path)
        {
            using (var fo = File.OpenWrite(path))
            {
                using (var gz = new GZipStream(fo, CompressionMode.Compress, true))
                {
                    var buffer = mgr.StructToArray();
                    gz.Write(buffer, 0, buffer.Length);
                }
                var pos = fo.Position;
                fo.Position = 8; // Header hack for spoofing the 3DS:
                fo.WriteByte(0); // Set compression to 0 instead of 4
                fo.WriteByte(3); // Set OS to UNIX instead of WINDOWS
                fo.Position = binSize - 4;
                fo.Write(BitConverter.GetBytes(pos), 0, 4); // Write compressed size at end
            }
        }

        public JbMgrFormat.JbMgrItem this[int index]
        {
            get { return mgr.Items[index]; }
            set { mgr.Items[index] = value; }
        }

        public bool Export(int i)
        {
            var item = this[i];
            var packpath = Path.Combine(Directory, $"gak\\{item.ID:x8}\\pack");
            if (!File.Exists(packpath)) return false;

            try
            {
                item.Scores = new byte[50];
                item.Singer = JbMgrFormat.JbMgrItem.JbSinger.None;
                item.Icon = JbMgrFormat.JbMgrItem.JbIcon.None;
                item.Flags.flag &= 0x7DFFFF;

                var itemData = item.StructToArray();
                var packData = File.ReadAllBytes(packpath);
                var titleWithoutNewlines = item.Title.Replace("\n", "");
                var bbpPath = Path.Combine("EXPORTFOLDER", $"{titleWithoutNewlines} ({item.Author}).bbp");

                using (var fo = File.Open(bbpPath, FileMode.Create))
                {
                    fo.Write(itemData, 0, itemData.Length);
                    fo.Write(packData, 0, packData.Length);
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }


        }
    }
}
