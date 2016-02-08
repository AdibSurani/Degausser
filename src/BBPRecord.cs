using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using Degausser.Utils;
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

        // A very quick hash function for testing purposes
        public string Hash => $"{QuickHash(bbp.StructToArray()) ^ QuickHash(mgrItem.StructToArray()):X16}";
        long QuickHash(byte[] buffer) => buffer.Aggregate((long)17, (hash, b) => hash * 23 + b);

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
                    title = item.Title.StringToArray(250),
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
                BIN2BBP.Convert(path, out mgrItem, out bbp, out vocaloid);
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
