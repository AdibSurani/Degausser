using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using static Degausser.Utils.Language;
using static Degausser.Utils.GZip;

namespace Degausser
{
    class BIN2BBP
    {
        class BinReader : BinaryReader
        {
            List<uint> stuff;
            int mapcount;

            public BinReader(string path) : base(File.OpenRead(path), Encoding.Unicode)
            {
                var filesize = BaseStream.Length;
                if (filesize < 20000000) throw new InvalidDataException($"{path} has incorrect filesize error #1");

                mapcount = ReadInt32();
                ReadInt32();
                if (mapcount * 12 > filesize) throw new InvalidDataException($"{path} has incorrect filesize error #2");
                stuff = Enumerable.Range(0, mapcount * 3).Select(_ => ReadUInt32()).ToList();
                var actualsize = Enumerable.Range(0, mapcount).Sum(i => stuff[i * 3 + 2]) + mapcount * 12 + 8;
                if (actualsize > filesize) throw new InvalidDataException($"{path} has incorrect filesize error #3");
            }

            public void Goto(uint addr)
            {
                // A tricky little function that converts the virtual memory addresses to offsets in the file
                var x = Enumerable.Range(0, stuff.Count / 3).Single(i => stuff[3 * i] <= addr && addr < stuff[3 * i] + stuff[3 * i + 2]);
                BaseStream.Position = addr - stuff[3 * x] + Enumerable.Range(0, x).Sum(i => stuff[3 * i + 2]) + mapcount * 12 + 8;
            }

            public string ReadStringPointer()
            {
                var oldaddr = BaseStream.Position;
                Goto(ReadUInt32());
                var sb = new StringBuilder();
                while (PeekChar() != 0) sb.Append(ReadChar());
                BaseStream.Position = oldaddr + 4;
                return sb.ToString();
            }
        }

        static readonly byte[] gzipHeaderBytes = { 1, 0, 2, 0, 1, 0, 0, 0, 60, 78, 1, 0, 68, 0, 0, 0 };

        public static void Convert(string path, out JbMgrFormat.JbMgrItem mgrItem, out BBPFormat bbp, out byte[] vocaloid)
        {
            mgrItem = JbMgrFormat.JbMgrItem.Empty;
            vocaloid = null;

            using (var br = new BinReader(path))
            {
                // Obtain ID2 and go look for all records in memory
                br.Goto(0xC16C68);
                mgrItem.OtherID = br.ReadUInt32();
                br.Goto(0xC16E90);
                br.Goto(br.ReadUInt32() + 4);
                br.Goto(br.ReadUInt32());
                br.Goto(br.ReadUInt32() + 56);
                br.Goto(br.ReadUInt32() + 24);
                br.Goto(br.ReadUInt32() + 12);

                // Go through records in memory to find the matching one
                uint recordCount = br.ReadUInt32();
                br.BaseStream.Position += 4;
                uint recordAddr = br.ReadUInt32();
                if (recordCount == 100)
                {
                    br.BaseStream.Position += 8;
                    recordCount = br.ReadUInt32();
                }
                br.Goto(recordAddr);
                while (br.ReadUInt32() != mgrItem.OtherID)
                {
                    if (--recordCount == 0) throw new InvalidDataException($"{path} has incorrect data -- could not find matching record");
                    br.BaseStream.Position += 48;
                }

                // Read mgrItem metadata from memory
                mgrItem.ID = br.ReadUInt32();
                mgrItem.Title = br.ReadStringPointer();
                if (mgrItem.Title.Length == 50 && mgrItem.Title[49] == '…')
                {
                    mgrItem.Title = mgrItem.Title.Substring(0, 49);
                }
                mgrItem.TitleSimple = br.ReadStringPointer().Simplify();
                br.BaseStream.Position += 8;
                mgrItem.Author = br.ReadStringPointer();
                br.BaseStream.Position += 12;
                var code = br.ReadStringPointer();

                // Finally, obtain pack data
                br.Goto(0xC16CF4);
                var packaddr = br.ReadUInt32();
                var packsize = br.ReadInt32();
                if (packsize >= 16777216) throw new InvalidDataException($"{path} has incorrect header error #5 {mgrItem.Title}:{mgrItem.Author}");
                br.Goto(packaddr);
                var bytes = br.ReadBytes(packsize);

                if (!gzipHeaderBytes.SequenceEqual(bytes.Take(16)))
                    throw new InvalidDataException($"{path} has incorrect data error #7 {mgrItem.Title}:{mgrItem.Author}");

                var nums = Enumerable.Range(0, 17).Select(i => BitConverter.ToInt32(bytes, i * 4)).ToList();

                //overwrite bbp's ID and title
                bbp = Decompress(bytes, nums[3], nums[4]).ToStruct<BBPFormat>();
                bbp.titleID = (int)mgrItem.ID;
                bbp.title = mgrItem.Title.StringToArray(bbp.title.Length);
                bbp.test0 = 1;
                bbp.test2 = (byte)(char.IsNumber(code[1]) ? 0 : 1);

                vocaloid = null;
                if (nums[5] == 1) vocaloid = Decompress(bytes, nums[7], nums[8]);
                //File.WriteAllBytes($"{path}.dat", bbp.StructToArray());
                //if (vocaloid != null) File.WriteAllBytes($"{path}.voc", vocaloid);

                // metadata stuff temporarily here
                var instrTypes = bbp.channelInfo.Select(c => c.playType);
                var hasPiano = instrTypes.Contains(PlayType.Piano);
                var hasGuitar = instrTypes.Contains(PlayType.Guitar);
                var hasDrum = instrTypes.Contains(PlayType.Drum);

                //mgr.Flags
                var instrx = bbp.timeSignature == 3;
                var lyrics = bbp.karaokeTimer[0] != 0x3FFF;
                var melody = bbp.test3 != -1;
                var isclassic = bbp.test2 == 1;

                mgrItem.Flags = new JbMgrFormat.JbMgrItem.JbFlags
                {
                    HasDrum = hasDrum,
                    HasGuitar = hasGuitar,
                    HasPiano = hasPiano,
                    HasLyrics = lyrics,
                    HasMelody = melody,
                    IsValid = true,
                    OnSD = true,
                    HasVocals = vocaloid != null,
                    IsSingable = vocaloid != null,
                    Parts = bbp.channelInfo.Count(c => c.instrument != 0),
                    IsReceived = true,
                    HasInstrX = instrx,
                    IsClassic = isclassic
                };
            }
        }
    }
}
