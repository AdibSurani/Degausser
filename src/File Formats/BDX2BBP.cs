using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using static Degausser.Utils.Language;

namespace Degausser
{
    class BDX2BBP
    {
        public static BBPFormat Convert(BDXFormat bdx, out JbMgrFormat.JbMgrItem mgr)
        {
            var converted = new BDX2BBP(bdx);
            mgr = converted.mgr;
            return converted.bbp;
        }

        readonly BDXFormat bdx;
        readonly BBPFormat bbp;
        readonly JbMgrFormat.JbMgrItem mgr;

        BDX2BBP(BDXFormat bdx)
        {
            this.bdx = bdx;
            bbp = new byte[Marshal.SizeOf<BBPFormat>()].ToStruct<BBPFormat>();
            DoCommonStuff();
            DoChannelStuff();
            var instrTypes = bbp.channelInfo.Select(c => c.playType);
            var hasPiano = instrTypes.Contains(PlayType.Piano);
            var hasGuitar = instrTypes.Contains(PlayType.Guitar);
            var hasDrum = instrTypes.Contains(PlayType.Drum);

            if (hasPiano && hasGuitar) throw new Exception("Not expecting both to exist!!!");

            if (hasPiano)
            {
                DoPianoStuff();
            }
            else if (hasGuitar)
            {
                DoGuitarStuff();
            }
            DoKaraokeStuff();
            // DoMetadataStuff() // mainly Author

            // metadata stuff temporarily here
            mgr = JbMgrFormat.JbMgrItem.Empty;
            //mgr.Author = bdx.contributor.ToString();
            mgr.Author = "Degausser2.2a";
            mgr.Title = bbp.title.ArrayToString();
            mgr.TitleSimple = mgr.Title;
            //mgr.Flags
            mgr.ID = 0x80000001;
            mgr.Flags = new JbMgrFormat.JbMgrItem.JbFlags
            {
                HasDrum = hasDrum,
                HasGuitar = hasGuitar,
                HasPiano = hasPiano,
                HasLyrics = bdx.hasKaraoke != 0,
                HasMelody = bdx.mainInstrument != 0xFF, // not sure about this
                IsValid = true,
                OnSD = true,
                Parts = bdx.channelInfo.Count(c => c.instrument != 0)
            };
        }

        void DoCommonStuff()
        {
            bbp.title = string.Concat(bdx.labels).Replace("\n", "").StringToArray(250);
            bbp.version = 0x20001;
            bbp.dateCreated = bdx.dateCreated;
            bbp.dateModified = bdx.dateModified;
            bbp.linesPlus6 = bdx.linesPlus6;
            bbp.timeSignature = bdx.timeSignature;
            bbp.masterVolumeMaybe = bdx.masterVolume;
            bbp.mainInstrumentMaybe = bdx.mainInstrument;

            bdx.tempoTimer.CopyTo(bbp.tempoChanges, 0); // tempo
        }

        void DoChannelStuff()
        {
            for (int i = 0; i < 8; i++)
            {
                bbp.channelInfo[i].cloneID = bdx.channelInfo[i].cloneID;
                bbp.channelInfo[i].env1 = bdx.channelEnvelopes[i];
                bbp.channelInfo[i].env2 = bdx.channelEnvelopes[i];
                bbp.channelInfo[i].instrument = bdx.channelInfo[i].instrument;
                bbp.channelInfo[i].playType = bdx.channelInfo[i].playType;
                bbp.channelInfo[i].volume = (byte)bdx.channelInfo[i].volume;
                bbp.channelInfo[i].masterProStar = bdx.channelInfo[i].masterProStar;
                bbp.channelInfo[i].amaBegiStar = bdx.channelInfo[i].amaBegiStar;
                //bbp.channelInfo[i].zero = 0; // ??
                bbp.channelInfo[i].eight = 8; // ??
                //bbp.channelInfo[i].otherInformation // has information about stars and stuff
                //bbp.channelInfo[i].unknown1 = 0; // ??
                //bbp.channelInfo[i].unknown2 = 0; // ??

                bdx.channelNotes[i].notes.CopyTo(bbp.channelNotes[i].notes, 0);

                bbp.panningMaybe[i].value = (short)(bdx.channelInfo[i].panning * 2);

                for (int j = 0; j < 32; j++)
                {
                    bbp.volumeChanges[i].changes[j] = new TimeValuePair
                    {
                        time = bdx.chanInfo5[i].stuff[j].time,
                        value = bdx.chanInfo5[i].stuff[j].volume
                    };
                }
                for (int j = 0; j < 600; j++)
                {
                    bbp.unknownMask[i].stuff[j] = (short)-1;
                }
                //bbp.unknownMask
                //bbp.volumeChanges
                //bbp.rangeChanges
                //bbp.effectorChanges // effectively zero, doesn't need changing
                foreach (var x in bbp.effectorChanges)
                {
                    x.changes[1].time = -1;
                }
            }
        }

        void DoPianoStuff()
        {
            // do piano chords first
            bbp.pianoPair = Enumerable.Repeat(new IndexRootPair { index = 0xFF, rawRoot = 0xFF }, 120).ToArray();
            //bbp.pianoOrig = new int[64];

            int next = bdx.pianoPair.Where(p => p.rawRoot == 0xFF).Max(p => (sbyte)p.index) + 1;
            Array.Copy(bdx.pianoOrig, bbp.pianoOrig, next);

            int minimum = 3 + (bdx.pianoVoicingStyle / 3);
            int spread = bdx.pianoVoicingStyle % 3; // don't know what this does yet
            int highestNote = bdx.pianoHighestNote == 0 ? 70 : bdx.pianoHighestNote;

            var dic = new Dictionary<short, IndexRootPair>();

            for (int i = 0; i < 32; i++)
            {
                var bdxpp = bdx.pianoPair[i];
                if (bdxpp.rawRoot == 0xFF)
                {
                    if (bdxpp.index == 0xFF) break;
                    bbp.pianoPair[i] = bdxpp;
                }
                else
                {
                    bbp.pianoPair[i] = new IndexRootPair { index = (byte)next, rawRoot = 0xFF };

                    var notes = (from basenote in new ArraySegment<byte>(InstrumentData.PianoChords, 4 * bdxpp.index, 4)
                                 let note = basenote == 0 ? 0 : ((basenote + bdxpp.Root) % 12 - highestNote) % 12 + highestNote
                                 orderby note descending
                                 select (byte)note)
                                 .ToArray();
                    if (notes[3] == 0 && minimum == 4)
                    {
                        notes[3] = (byte)(notes[0] - 12);
                    }
                    bbp.pianoOrig[next] = BitConverter.ToInt32(notes, 0);
                    dic.Add(bdxpp.Short, bbp.pianoPair[i]);
                    next++;
                }
            }

            bbp.pianoChordChangesCount = bdx.chordChanges;
            for (int i = 0; i < bbp.pianoChordChangesCount; i++)
            {
                var pair = bdx.chordTimer[i];
                if (pair.value >> 8 != -1)
                {
                    pair.value = dic[pair.value].Short;
                }
                bbp.pianoChordChangeTable[i] = pair;
            }
            //bdx.chordTimer.CopyTo(bbp.pianoChordChangeTable, 0);
        }

        void DoGuitarStuff()
        {
            bdx.guitarOrig.CopyTo(bbp.guitarOrig, 0);
            for (int i = 0; i < 32; i++)
            {
                bbp.guitarTimer[i].time = bdx.guitarTimer[i].time;
                for (int j = 0; j < 10; j++)
                {
                    var oldpair = bdx.guitarTimer[i].pair[j];
                    int index = oldpair.index;
                    int rawRoot = oldpair.rawRoot;
                    //({ index * 16 + 15}, { ((rawRoot << 5) | (rawRoot >> 3)) & 0xFF})
                    bbp.guitarTimer[i].pair[j] = new IndexRootPair
                    {
                        index = (byte)(index * 16 + 15),
                        rawRoot = (byte)((rawRoot << 5) | (rawRoot >> 3))
                    };
                }
            }

            // todo: consolidate the above and below functions

            bbp.guitarChordChangesCount = bdx.chordChanges;
            for (int i = 0; i < bdx.chordChanges; i++)
            {
                bbp.guitarChordChangeTable[i].time = bdx.chordTimer[i].time;
                var bytes = BitConverter.GetBytes(bdx.chordTimer[i].value);
                bytes[0] = (byte)(bytes[0] * 16 + 15);
                bytes[1] = (byte)((bytes[1] << 5) | (bytes[1] >> 3));
                bbp.guitarChordChangeTable[i].value = BitConverter.ToInt16(bytes, 0);
            }
        }
        
        void DoKaraokeStuff()
        {
            if (bdx.hasKaraoke == 0)
            {
                bbp.karaokeTimer[0] = 0x3FFF;
            }
            else
            {
                var tmp = GetBDXJPString(bdx.karaokeLyrics, true);
                //bbp.karaokeLyrics = tmp;
                //bdx.karaokeTimer.CopyTo(bbp.karaokeTimer, 0);
                int m = Array.FindIndex<short>(bdx.karaokeTimer, t => (t & 0x3FFF) == 0x3FFF);

                short prev = -1;
                var sb = new StringBuilder();
                for (int i = 0; i < 2048; i++)
                {
                    if (tmp[i] == zeroWidthSpace) continue;
                    short curr = (short)(bdx.karaokeTimer[i] & 0x3FFF);

                    if (curr == 0x3FFF)
                    {
                        // ended
                        bbp.karaokeTimer[sb.Length] = curr;
                        break;
                    }

                    bbp.karaokeTimer[sb.Length] = (short)(curr | 0x8000);
                    if (curr == prev)
                    {
                        bbp.karaokeTimer[sb.Length] |= 0x4000;
                    }
                    else
                    {
                        prev = curr;
                    }
                    sb.Append(tmp[i]);
                }
                bbp.karaokeLyrics = sb.ToString().StringToArray(3000);

            }
        }
    }
}
