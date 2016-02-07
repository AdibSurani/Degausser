using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using static Degausser.Utils.GZip;

namespace Degausser
{
    class BBPRecord
    {
        // Quick information
        public string FullPath { get; }
        public string Filename => Path.GetFileName(FullPath);
        public string Folder => Path.GetFileName(Path.GetDirectoryName(FullPath));
        public string Title => mgrItem.Title.Replace("\n", "");
        public string Author => mgrItem.Author;
        public bool HasKaraoke => mgrItem.Flags.HasLyrics;
        public bool HasGuitar => mgrItem.Flags.HasGuitar;
        public bool HasPiano => mgrItem.Flags.HasPiano;
        public int Lines => bbp.linesPlus6 - 6;
        public int Instruments => bbp.channelInfo.Count(c => c.instrument != 0);
        public int Slot { get; }

        public JbMgrFormat.JbMgrItem mgrItem;
        public BBPFormat bbp;
        public byte[] vocaloid; // uncompressed

        // From known item and packpath
        public BBPRecord(JbMgrFormat.JbMgrItem item, string path, int slot)
        {
            Slot = slot;
            FullPath = path;
            mgrItem = item;
            if (item.Flags.OnSD)
            {
                var buffer = File.ReadAllBytes(path);
                var bytes = buffer;
                var nums = Enumerable.Range(0, 17).Select(i => BitConverter.ToInt32(bytes, i * 4)).ToList();

                var unc1 = Decompress(bytes, nums[3], nums[4]);
                bbp = unc1.ToStruct<BBPFormat>();
                if (nums[5] == 1) vocaloid = Decompress(bytes, nums[7], nums[8]);
            }
            else
            {
                bbp = new BBPFormat
                {
                    title = item.Title.ToCharArray(),
                    channelInfo = new BBPFormat.ChannelInfo[0]
                };
            }
        }

        // From a file
        public BBPRecord(string path)
        {
            
            FullPath = path;
            if (Path.GetExtension(path).ToLower() == ".bin")
            {
                using (var br = new BinaryReader(File.OpenRead(path)))
                {

                    var filesize = br.BaseStream.Length;
                    if (filesize < 20000000) throw new InvalidDataException($"{path} has incorrect filesize error #1");

                    int mapcount = br.ReadInt32();
                    br.ReadInt32();
                    if (mapcount * 12 > filesize) throw new InvalidDataException($"{path} has incorrect filesize error #2");
                    var stuff = Enumerable.Range(0, mapcount * 3 + 1).Select(_ => br.ReadUInt32()).ToList();
                    var actualsize = Enumerable.Range(0, mapcount ).Sum(i => (long)stuff[i * 3 + 2]) + mapcount * 12 + 8;
                    //if (actualsize != filesize) throw new InvalidDataException($"{path} has incorrect filesize error #3");
                    
                    var h = 0x7b7fff8 - mapcount * 12;
                    var offset = 0x8900000 - h;
                    var titlead = 0L;
                    var simplead = 0L;
                    var authorad = 0L;
                    var codead = 0L;
                    string code;
                    string title2 = null;   
                    string simple = null;   // title in Katakana, a-z, and so on. this can not use various charactors
                    string author2 = null;
                    uint id1 = 0;   //ID
                    uint id2 = 0;   //OtherID
                    using (var br2 = new BinaryReader(File.OpenRead(path), Encoding.GetEncoding("Unicode")))
                    {
                        try {
                            //byte search
                            int b;
                            int[] binary = new int[] { 0x44, 0x55, 0x00, 0x00, 0x44, 0x06, 0x00, 0x00 };
                            int m = 0;


                            br2.BaseStream.Seek(0x600000, SeekOrigin.Begin);
                            while (m != binary.Length && (b = br2.ReadByte()) != -1)
                            {
                                if (b == binary[m])
                                {
                                    m++;
                                }
                                else
                                {
                                    m = 0;
                                }
                            }
                            if (m == binary.Length)
                            {
                                br2.BaseStream.Seek(120, SeekOrigin.Current);
                                authorad = br2.ReadUInt32();
                                br2.BaseStream.Seek(0xcd0000, SeekOrigin.Begin);
                                while ((titlead <= 0x8850000 || titlead >= 0x8890000) || (simplead <= 0x8850000 || simplead >= 0x8890000))
                                {
                                    while (br2.ReadUInt32() != authorad)
                                    {
                                    }
                                    br2.BaseStream.Seek(-20, SeekOrigin.Current);
                                    titlead = br2.ReadUInt32();
                                    simplead = br2.ReadUInt32();
                                    br2.BaseStream.Seek(12, SeekOrigin.Current);
                                }
                                br2.BaseStream.Seek(-28, SeekOrigin.Current);
                                id2 = br2.ReadUInt32();
                                id1 = br2.ReadUInt32();
                                br2.BaseStream.Seek(32, SeekOrigin.Current);
                                codead = br2.ReadUInt32();


                                br2.BaseStream.Seek(titlead - h, SeekOrigin.Begin);
                                char c;
                                int cnt = 0;
                                while ((c = br2.ReadChar()) != '\0')
                                {
                                    cnt++;
                                }
                                br2.BaseStream.Seek(cnt * -2 - 2, SeekOrigin.Current);
                                title2 = new string(br2.ReadChars(cnt));

                                br2.BaseStream.Seek(simplead - h, SeekOrigin.Begin);
                                cnt = 0;
                                while ((c = br2.ReadChar()) != '\0')
                                {
                                    cnt++;
                                }
                                char[] cs = new char[cnt + 1];
                                br2.BaseStream.Seek(cnt * -2 - 2, SeekOrigin.Current);
                                cnt = 0;
                                while ((c = br2.ReadChar()) != '\0')
                                {
                                    if(c >= 0x3041 && c <= 0x3094)
                                    {
                                        c = (char)((uint)c + 0x60);
                                    }
                                    if (c == 0x3000 || c == 0xffe5 || c == 0x2019)  // ' ￥
                                    {
                                        cnt--;
                                    }
                                    else
                                    {
                                        if ((c >= 0x30a1 && c <= 0x30a9) || c == 0x30c3 || (c >= 0x30e3 && c <= 0x30e7))
                                        {
                                            if (c % 2 == 1) //ァィゥェォッャュョ
                                            {
                                                c++;
                                            }
                                        }
                                        else if (c >= 0x30ac && c <= 0x30c2)
                                        {
                                            if (c % 2 == 0) //ガギグゲゴザジズゼゾダヂ
                                            {
                                                c--;
                                            }
                                        }
                                        else if (c >= 0x30c5 && c <= 0x30c9)
                                        {
                                            if (c % 2 == 1) //ヅデド
                                            {
                                                c--;
                                            }
                                        }
                                        else if (c >= 0x30d0 && c <= 0x30dd)
                                        {
                                            if (c % 3 == 1) //バビブベボ
                                            {
                                                c--;
                                            }
                                            else if (c % 3 == 2)    //パピプペポ
                                            {
                                                c--;
                                                c--;
                                            }
                                        }
                                        else if (c >= 0xff21 && c <= 0xff3a)    //Ａ-Ｚ
                                        {
                                            c = (char)((uint)c - 0xfec0);
                                        }
                                        else if ((c >= 0xff41 && c <= 0xff5a) || (c >= 0xff01 && c <= 0xff20) || (c >= 0xff3b && c <= 0xff3f) || (c >= 0xff5b && c <= 0xff5e)) //ａ-ｚ, signs
                                        {
                                            c = (char)((uint)c - 0xfee0);
                                        }
                                        else if (c >= 0x0041 && c <= 0x005a)    //A-Z
                                        {
                                            c = (char)((uint)c + 0x20);
                                        }
                                        else if (c == 0x30f4)   //ヴ
                                        {
                                            c = (char)((uint)c - 0x4e);
                                        }
                                        else if(c == 0x30fc)    //ー
                                        {
                                            if ((new System.Text.RegularExpressions.Regex(@"[\u30a2\u30ab\u30b5\u30bf\u30ca\u30cf\u30de\u30e4\u30e9\u30ef]")).IsMatch(cs[cnt - 1].ToString()))
                                            {
                                                c = (char)0x30a2;
                                            }
                                            else if((new System.Text.RegularExpressions.Regex(@"[\u30a4\u30ad\u30b7\u30c1\u30cb\u30d2\u30df\u30ea]")).IsMatch(cs[cnt - 1].ToString()))
                                            {
                                                c = (char)0x30a4;
                                            }
                                            else if((new System.Text.RegularExpressions.Regex(@"[\u30a6\u30af\u30b9\u30c4\u30cc\u30d5\u30e0\u30e6\u30eb]")).IsMatch(cs[cnt - 1].ToString()))
                                            {
                                                c = (char)0x30a6;
                                            }
                                            else if((new System.Text.RegularExpressions.Regex(@"[\u30a8\u30b1\u30bb\u30c6\u30cd\u30d8\u30e1\u30ec]")).IsMatch(cs[cnt - 1].ToString()))
                                            {
                                                c = (char)0x30a8;
                                            }
                                            else if ((new System.Text.RegularExpressions.Regex(@"[\u30aa\u30b3\u30bd\u30c8\u30ce\u30db\u30e2\u30e8\u30ed]")).IsMatch(cs[cnt - 1].ToString()))
                                            {
                                                c = (char)0x30aa;
                                            }
                                        }

                                        cs[cnt] = c;
                                    }
                                    cnt++;
                                }
                                cs[cnt] = '\0';
                                simple = new string(cs);

                                br2.BaseStream.Seek(authorad - h, SeekOrigin.Begin);
                                cnt = 0;
                                while ((c = br2.ReadChar()) != '\0')
                                {
                                    cnt++;
                                }
                                br2.BaseStream.Seek(cnt * -2 - 2, SeekOrigin.Current);
                                author2 = new string(br2.ReadChars(cnt));

                                br2.BaseStream.Seek(codead - h, SeekOrigin.Begin);
                                code = new string(br2.ReadChars(3));
                            }
                            else
                            {
                                throw new InvalidDataException($"{path} has incorrect data" + title2 + ":" + author2);
                            }
                        }
                        catch (EndOfStreamException)
                        {
                            throw new InvalidDataException($"{path} has incorrect data" + title2 + ":" + author2);
                        }
                    }
                    // offset = Enumerable.Range(0, mapcount + 1).TakeWhile(i => (long)stuff[i * 3] != 0x8900000).Sum(i => (long)stuff[i * 3 + 2]) + mapcount * 12 + 8;

                    // all filesizes are good!
                    br.BaseStream.Position = offset;
                    if (br.ReadInt32() != 0x5544) throw new InvalidDataException($"{path} has incorrect header error #4" + title2 + ":" + author2);
                    var packsize = br.ReadInt32();
                    if (packsize >= 16777216) throw new InvalidDataException($"{path} has incorrect header error #5" + title2 + ":" + author2);

                    br.ReadInt64();
                    //if (br.ReadInt64() != 0) throw new InvalidDataException($"{path} has incorrect header error #6");
                    var bytes = br.ReadBytes(packsize);

                    if (!new byte[] { 1, 0, 2, 0, 1, 0, 0, 0, 60, 78, 1, 0, 68, 0, 0, 0 }.SequenceEqual(bytes.Take(16)))
                        throw new InvalidDataException($"{path} has incorrect data error #7" + title2 + ":" + author2);

                    var nums = Enumerable.Range(0, 17).Select(i => BitConverter.ToInt32(bytes, i * 4)).ToList();

                    var unc1 = Decompress(bytes, nums[3], nums[4]);
                    
                    //overwrite unc1's ID and title
                    
                    byte[] idb = BitConverter.GetBytes(id1);
                    for(int i = 0; i < 4; i++)
                    {
                        unc1[i + 4] = idb[i];
                    }
                    byte[] bs2 = Encoding.Unicode.GetBytes(title2);
                    for (int i = 0; (unc1[i + 16] != 0 || unc1[i + 17] != 0) || i < bs2.Length; i += 2)
                    {
                        if(i < bs2.Length)
                        {
                            unc1[i + 16] = bs2[i];
                            unc1[i + 17] = bs2[i + 1];
                        }
                        else
                        {
                            unc1[i + 16] = 0;
                            unc1[i + 17] = 0;
                        }
                    }
                    unc1[0x204] = 1;    //unknown bit
                    if((new System.Text.RegularExpressions.Regex(@"[0-9][^0-9][0-9]")).IsMatch(code))
                    {
                        unc1[0x206] = 1;
                    }

                    using (BinaryWriter writer = new BinaryWriter(File.Open(path + ".dat", FileMode.Create)))
                    {
                        for (int i = 0; i < unc1.Length; i++)
                        {
                            writer.Write(unc1[i]);
                        }
                    }
                    
                    bbp = unc1.ToStruct<BBPFormat>();
                    if (nums[5] == 1) vocaloid = Decompress(bytes, nums[7], nums[8]);
                    /*
                    if (vocaloid != null)
                    {
                        using (BinaryWriter writer = new BinaryWriter(File.Open(path + ".voc", FileMode.Create)))
                        {
                            for (int i = 0; i < vocaloid.Length; i++)
                            {
                                writer.Write(vocaloid[i]);
                            }
                        }
                    }*/

                    // metadata stuff temporarily here
                    var instrTypes = bbp.channelInfo.Select(c => c.playType);
                    var hasPiano = instrTypes.Contains(PlayType.Piano);
                    var hasGuitar = instrTypes.Contains(PlayType.Guitar);
                    var hasDrum = instrTypes.Contains(PlayType.Drum);
                    mgrItem = JbMgrFormat.JbMgrItem.Empty;
                    mgrItem.Author = author2;
                    mgrItem.Title = title2;
                    mgrItem.TitleSimple = simple;
                    mgrItem.ID = id1;
                    mgrItem.OtherID = id2;

                    //mgr.Flags
                    
                    Boolean instrx = false;
                    Boolean lyrics = true;
                    Boolean melody = true;
                    Boolean isclassic = false;
                    if (unc1[0x1139c] == 0xff && unc1[0x1139d] == 0x3f)
                    {
                        lyrics = false;
                    }
                    if(unc1[0x207] == 0xff)
                    {
                        melody = false; 
                    }
                    if(unc1[0x209] == 0x03) //quadruple or triple time
                    {
                        instrx = true;
                    }
                    if(unc1[0x206] == 0x1)
                    {
                        isclassic = true;
                        //Debug.WriteLine("classic");
                    }
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
            else if (Path.GetExtension(path).ToLower() == ".bdx")
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length != 32768)
                {
                    throw new InvalidDataException($"{path} has an incorrect filesize!");
                }
                var bdx = bytes.ToStruct<BDXFormat>();
                bbp = BDX2BBP.Convert(bdx, out mgrItem);
                //mgrItem = new JbMgrFormat.JbMgrItem() { Author = bdx.contributor.ToString() };
            }
            else
            {
                var buffer = File.ReadAllBytes(path);
                mgrItem = buffer.Take(312).ToArray().ToStruct<JbMgrFormat.JbMgrItem>();
                var bytes = buffer.Skip(312).ToArray();
                var nums = Enumerable.Range(0, 17).Select(i => BitConverter.ToInt32(bytes, i * 4)).ToList();

                var unc1 = Decompress(bytes, nums[3], nums[4]);
                bbp = unc1.ToStruct<BBPFormat>();
                if (nums[5] == 1) vocaloid = Decompress(bytes, nums[7], nums[8]);
            }
        }

        public void SaveAsBBPFile(string path)
        {
            var item = mgrItem;
            item.Scores = new byte[50];
            item.Singer = JbMgrFormat.JbMgrItem.JbSinger.None;
            item.Icon = JbMgrFormat.JbMgrItem.JbIcon.None;
            item.Flags.flag &= 0x7FDFFF;
            var itemData = item.StructToArray();
            var packData = Recompile();

            using (var fo = File.Open(path, FileMode.Create))
            {
                fo.Write(itemData, 0, itemData.Length);
                fo.Write(packData, 0, packData.Length);
            }
        }

        public void SaveAsPackFile(string path)
        {
            File.WriteAllBytes(path, Recompile());
        }

        public byte[] Recompile()
        {
            var unc1 = bbp.StructToArray();
            var unc2 = vocaloid;
            byte[] result;

            var c1 = Compress(unc1);
            var nums = new int[17];
            nums[0] = 0x20001;
            nums[1] = 1;
            nums[2] = unc1.Length;
            nums[3] = 68;
            nums[4] = c1.Length;

            if (unc2 == null)
            {
                result = new byte[c1.Length + 68];
            }
            else
            {
                int c1len4 = (c1.Length + 3) & ~3;
                var c2 = Compress(unc2);
                nums[5] = 1;
                nums[6] = unc2.Length;
                nums[7] = 68 + c1len4;
                nums[8] = c2.Length;
                result = new byte[c1len4 + c2.Length + 68];
                c2.CopyTo(result, 68 + c1len4);
            }
            c1.CopyTo(result, 68);
            
            for (int i = 0; i < 17; i++)
            {
                BitConverter.GetBytes(nums[i]).CopyTo(result, i * 4);
            }
            return result;
        }

        MidiPlayer.MidiData midiData;
        public MidiPlayer.MidiData GetMidiData()
        {
            if (midiData == null)
            {
                SetUpMidi();
            }
            return midiData;
        }

        public string GetInstrumentName(int chanNumber)
        {
            var c = bbp.channelInfo[chanNumber];
            int u = c.instrument;
            if (u == 0) return "(Blank)";
            string clone = c.cloneID > 0 ? " " + c.cloneID : null;
            return $"{InstrumentData.Instruments[u].Name}{clone} ({c.playType})";
        }

        static void RunChangeTracker(TimeValuePair[] array, int length, Action<int, short> action)
        {
            for (int i = 0, cursor = 0; i < length; i++)
            {
                if (cursor < array.Length - 1 && i == (array[cursor + 1]).time)
                {
                    cursor++;
                }
                action(i, array[cursor].value);
            }
        }

        static IEnumerable<short> ChangeTracker(IList<TimeValuePair> array, int length)
        {
            for (int i = 0, cursor = 0; i < length; i++)
            {
                if (cursor < array.Count - 1 && i == (array[cursor + 1]).time)
                {
                    cursor++;
                }
                yield return array[cursor].value;
            }
        }

        void SetUpMidi()
        {
            midiData = new MidiPlayer.MidiData(ChangeTracker(bbp.tempoChanges, Lines * 48).ToArray());

            for (int i = 0; i < 10; i++)
            {
                // volume as well??
                var volume = (from item in ChangeTracker(bbp.volumeChanges[i].changes, Lines * 48)
                              let vol = item * bbp.channelInfo[i].volume * (bbp.masterVolumeMaybe + 64)
                              select (byte)Math.Min(127, vol >> 14))
                              .ToList();

                var c = midiData.Channels[i];
                var info = bbp.channelInfo[i];
                var instr = InstrumentData.Instruments[info.instrument];
                c.InstrumentMidi = instr.Midi;
                c.InstrumentName = instr.Name;
                var drum = instr.DrumNotes;

                // Adjust instrument name
                if (c.InstrumentMidi < 128) // 0-127 are real MIDI instruments
                {
                    if (info.cloneID > 0) c.InstrumentName += $" {info.cloneID}";
                    c.InstrumentName += $" ({info.playType})";
                }

                List<IndexRootPair> pianoTracker;
                if (info.playType == PlayType.Piano)
                {
                    var pianoChordSegment = new ArraySegment<TimeValuePair>(bbp.pianoChordChangeTable, 0, bbp.pianoChordChangesCount);
                    pianoTracker = (from val in ChangeTracker(pianoChordSegment, Lines * 48)
                                    select new IndexRootPair { index = val.HiByte(), rawRoot = val.LoByte() })
                                    .ToList();
                }

                // do the guitar remapping at this point

                for (int j = 0; j < Lines * 4; j++)
                {
                    Action<byte, IEnumerable<byte>, Action<int>, Action<int, byte, byte>> DoThreeIf = (cmp, notes, releaseAction, playAction) =>
                    {
                        int frame = j * 12;
                        int isThreeNotes = notes.First() == cmp ? 1 : 0;

                        foreach (var note in notes.Skip(isThreeNotes))
                        {
                            if (note < 128)
                            {
                                releaseAction(frame);
                                if (note > 0) playAction(frame, note, volume[frame]);
                            }
                            frame += 3 + isThreeNotes;
                        }
                    };

                    var seg = new ArraySegment<byte>(bbp.channelNotes[i].notes, j * 4, 4);
                    switch (bbp.channelInfo[i].playType)
                    {
                        case PlayType.Standard:
                            DoThreeIf(0xFF, seg, c.ReleaseNote, c.AddNote);
                            break;
                        case PlayType.Drum:
                            DoThreeIf(0xF, seg.Select(x => (byte)(x & 15)), _ => { }, (frame, note, vol) => c.AddDrum(frame, drum[note], vol));
                            DoThreeIf(0xF, seg.Select(x => (byte)(x >> 4)), _ => { }, (frame, note, vol) => c.AddDrum(frame, drum[note], vol));
                            break;
                        case PlayType.Guitar:
                            break;
                        case PlayType.Piano:
                            //DoThreeIf(0xFF, seg, c.ReleaseChord, (frame, note, vol) => c.AddChord(frame, new PianoChord(bbp.pianoOrig[note - 1]), vol));
                            break;
                    }
                }
            }

        }

        const byte FAKE_VOLUME = 80;

        class GuitarChord : MidiPlayer.Chord
        {
            int bdxData;

            public GuitarChord(int data)
            {
                bdxData = data;
            }

            public GuitarChord(byte rootNote, int mapIndex)
            {
                bdxData = BitConverter.ToInt32(InstrumentData.GuitarChords, 52 * rootNote + 4 * mapIndex);
            }

            public override byte[] Notes
            {
                get
                {
                    byte[] notes = new byte[6];
                    for (int i = 0; i < 6; i++)
                    {
                        int newnote = bdxData >> (5 * i) & 31;
                        notes[i] = (byte)(newnote < 16 ? InstrumentData.GuitarTuning[i] + newnote : 0);
                    }
                    return notes;
                }
            }
        }

        void AddGuitarNotes(MidiPlayer.MidiChannel c, int frame, IEnumerable<byte> notes)
        {
            int isThreeNotes = notes.First() == 0xFF ? 1 : 0;

            foreach (var note in notes.Skip(isThreeNotes))
            {
                if (note < 128)
                {
                    c.ReleaseChord(frame);
                    var i = note / 4;
                    int remapped = 0;
                    if (note > 0) c.AddChord(frame, new GuitarChord(remapped), FAKE_VOLUME);
                }
                frame += 3 + isThreeNotes;
            }
        }

        class PianoChord : MidiPlayer.Chord
        {
            public PianoChord(int encodedNotes)
            {
                Notes = BitConverter.GetBytes(encodedNotes);
            }
        }

    }
}
