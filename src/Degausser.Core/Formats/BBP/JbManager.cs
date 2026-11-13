using System.IO;
using Kermalis.EndianBinaryIO;

namespace Degausser
{
    public class JbManager
    {
        const int binSize = 1155072;

        public JbMgr mgr;
        public string FilePath { get; }
        public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;

        public JbManager(string path)
        {
            FilePath = path;
            mgr = JbMgr.Read(ExtDataSafeFile.ReadDecompressed(path));
        }

        public void Save()
            => ExtDataSafeFile.WriteMirrored(FilePath, mgr.ToBytes(), binSize);
    }
}
