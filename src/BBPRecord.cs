using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace Degausser
{
    class BBPRecord
    {
        // Quick information
        public string FullPath { get; private set; }
        public string Filename => Path.GetFileName(FullPath);
        public string Folder => Path.GetFileName(Path.GetDirectoryName(FullPath));
        public string Title => mgrItem.title.Replace("\n", "");
        public string Author => mgrItem.author;
        public bool HasKaraoke => mgrItem.flags.HasLyrics;
        public bool HasGuitar => mgrItem.flags.HasGuitar;
        public bool HasPiano => mgrItem.flags.HasPiano;
        public int Lines => gak.linesPlus6 - 6;
        public int Instruments => gak.channelInfo.Count(c => c.instrument != 0);
        public int Slot { get; private set; }

        // A very quick hash function for testing purposes
        public string Hash => $"{QuickHash(gak.StructToArray()) ^ QuickHash(mgrItem.StructToArray()):X16}";
        long QuickHash(byte[] buffer) => buffer.Aggregate((long)17, (hash, b) => hash * 23 + b);

        public JbMgr.Item mgrItem;
        public Gak gak;
        public byte[] vocaloid;

        // From known item and packpath
        public static BBPRecord FromJbMgr(JbMgr.Item item, string path, int slot)
        {
            return new BBPRecord(File.ReadAllBytes(path))
            {
                FullPath = path,
                mgrItem = item,
                Slot = slot
            };
        }

        // From a BBP file
        public static BBPRecord FromBBP(string path)
        {
            var buffer = File.ReadAllBytes(path);
            return new BBPRecord(buffer, 312)
            {
                FullPath = path,
                mgrItem = buffer.ToStruct<JbMgr.Item>()
            };
        }

        // From a "pack" file
        public static BBPRecord FromPack(string path)
        {
            return new BBPRecord(File.ReadAllBytes(path))
            {
                FullPath = path
            };
        }

        BBPRecord(byte[] bytes, int offset = 0)
        {
            var nums = Enumerable.Range(0, 17).Select(i => BitConverter.ToInt32(bytes, i * 4 + offset)).ToList();
            gak = bytes.Decompress(nums[3] + offset, nums[4]).ToStruct<Gak>();
            if (nums[5] == 1) vocaloid = bytes.Decompress(nums[7] + offset, nums[8]);
        }

        public void SaveAsBBPFile(string path)
        {
            var item = mgrItem;
            item.scores = new byte[50];
            item.singer = JbMgr.Item.JbSinger.None;
            item.icon = JbMgr.Item.JbIcon.None;
            item.flags.flag &= 0x7FDFFF;

            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write(item.StructToArray());
                bw.Write(Recompile());
            }
        }

        public void SaveAsPackFile(string path)
        {
            File.WriteAllBytes(path, Recompile());
        }

        public byte[] Recompile()
        {
            var unc1 = gak.StructToArray();
            var unc2 = vocaloid;
            byte[] result;

            var c1 = unc1.Compress();
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
                var c2 = unc2.Compress();
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
            var c = gak.channelInfo[chanNumber];
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
            midiData = new MidiPlayer.MidiData(ChangeTracker(gak.tempoChanges, Lines * 48).ToArray());

            for (int i = 0; i < 10; i++)
            {
                // volume as well??
                var volume = (from item in ChangeTracker(gak.volumeChanges[i].changes, Lines * 48)
                              let vol = item * gak.channelInfo[i].volume * (gak.masterVolumeMaybe + 64)
                              select (byte)Math.Min(127, vol >> 14))
                              .ToList();

                var c = midiData.Channels[i];
                var info = gak.channelInfo[i];
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
                    var pianoChordSegment = new ArraySegment<TimeValuePair>(gak.pianoChordChangeTable, 0, gak.pianoChordChangesCount);
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

                    var seg = new ArraySegment<byte>(gak.channelNotes[i].notes, j * 4, 4);
                    switch (gak.channelInfo[i].playType)
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
