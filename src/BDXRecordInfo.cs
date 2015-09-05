using System;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using Degausser.Properties;
using System.Runtime.InteropServices;
using System.Linq;

namespace Degausser
{
    public class BDXInformation
    {
        public string Filename { get; set; }
        public string FullPath { get; set; }
        public string Title { get; set; }
        public int Lines { get; set; }
        public bool IsKaraoke { get; set; }
        public string Folder { get; set; }
        public string Contributor { get; set; }
        public TimeSpan Duration { get; set; }
    }

    partial class BDXRecord
    {
        #region A bunch of BDX stuff
        int lines;
        ChangeList<TimeValuePair> tempoTimer;
        short[] tempoValue = new short[32];
        GuitarChord[] guitarOriginal = new GuitarChord[16];
        ChangeTracker guitarTimer = new ChangeTracker(32);
        IndexRootPair[][] guitarMapIndexRootPairs = new IndexRootPair[32][];
        byte[] karaokeLyrics;
        short[] karaokeTimer = new short[2048];
        PianoChord[] pianoOriginal = new PianoChord[32];
        IndexRootPair[] pianoMapIndexRootPair = new IndexRootPair[32];
        ChangeTracker chordTimer = new ChangeTracker(255);
        int[] chordIndex = new int[255];
        PianoChord[] remappedChords = new PianoChord[32];
        GuitarChord[,] remappedGuitar = new GuitarChord[32, 9];
        #endregion

        class BDXChannel
        {
            public int volume;
            public int instrument;
            public PlayType playType;
            public int panning;
            public int master, pro, amateur, beginner;
            public int cloneID;
            public byte[] notes;
            public ChangeTracker changeTimer = new ChangeTracker(32);
            public int[] changeValue = new int[32];
            public int[] changeVolume = new int[32];
            public int[] changeType = new int[32];
            //public byte[] clef;
        };
        
        /*
         * To be used in things with changes denoted by time, such as:
         * tempo changes (ended by ffff)
         * key/volume changes (ended by ffff)
         * guitar chord map changes (ended by ffff)
         * karaoke lyrics (ended by 3fff)
         * chord arrangements in composer (counted)
         */
        class ChangeTracker
        {
            short[] times;
            int internalCursor = 0;
            int externalCursor = 0;

            public ChangeTracker(int n)
            {
                times = new short[n];
            }

            public void AddTime(short time) => times[internalCursor++] = time;

            public int this[int index]
            {
                get
                {
                    if (externalCursor + 1 < times.Length)
                    {
                        if (index == times[externalCursor + 1])
                        {
                            externalCursor++;
                        }
                    }
                    return externalCursor;
                }
            }
        }

        class ChangeList<T>
        {
            T[] BackingArray;
            int[] lookup;

            public ChangeList(T[] source, int max)
            {
                BackingArray = source;
                var times = BackingArray.Select(x => (int)((dynamic)x).time).ToList();
                times.Add(int.MaxValue);

                lookup = new int[max];
                for (int i = 0, cursor = 0; i < max; i++)
                {
                    if (i == times[cursor + 1])
                    {
                        cursor++;
                    }
                    lookup[i] = cursor;
                }
            }

            public T GetSnapshotAtTime(int time)
            {
                return BackingArray[lookup[time]];
            }
        }

        public BDXRecord(string path)
        {
            bdx = File.ReadAllBytes(path).ToStruct<BDXFormat>();
            Information = new BDXInformation
            {
                Lines = bdx.linesPlus6 - 6,
                Title = String.Concat(bdx.labels).Replace("\n", ""),
                Contributor = bdx.contributor.ToString(),
                IsKaraoke = bdx.hasKaraoke != 0,
                Filename = Path.GetFileName(path),
                Folder = Path.GetFileName(Path.GetDirectoryName(path)),
                FullPath = path
            };
        }

        public static BDXInformation GetInformation(string path)
        {
            // to be replaced with a lightweight version
            return new BDXRecord(path).Information;
        }

        public /* SHOULD NOT BE PUBLIC */ BDXFormat bdx;

        public BDXInformation Information { get; } = new BDXInformation();

        // Extracts information from the GAK Header and BDX Header and uncompressed data
        void ParseData()
        {
            lines = Information.Lines;

            // Populate channel info
            for (int i = 0; i < 8; i++)
            {
                var c = bdx.channelInfo[i];
                var ch = channel[i] = new BDXChannel
                {
                    volume = c.volume,
                    //instrument = InstrumentMap(c.Instrument),
                    instrument = c.instrument,
                    playType = (PlayType)c.playType,
                    panning = c.panning,
                    master = c.masterProStar % 16,
                    pro = c.masterProStar / 16,
                    cloneID = c.cloneID,
                    amateur = c.amaBegiStar % 16,
                    beginner = c.amaBegiStar / 16,
                    notes = bdx.channelNotes[i].notes
                    //clef = bdx.keySig[i].stuff
                };
                for (int j = 0; j < 8; j++)
                {
                    var c5 = bdx.chanInfo5[i].stuff[j];
                    ch.changeTimer.AddTime(c5.time);
                    ch.changeValue[j] = c5.value;
                    ch.changeVolume[j] = c5.volume;
                    ch.changeType[j] = c5.type;
                }
            }

            tempoTimer = new ChangeList<TimeValuePair>(bdx.tempoTimer, lines * 48);

            for (int i = 0; i < 32; i++)
            {
                //tempoTimer.AddTime(bdx.TempoTimer[i].Time);
                //tempoValue[i] = bdx.TempoTimer[i].Value;

                guitarTimer.AddTime(bdx.guitarTimer[i].time);
                guitarMapIndexRootPairs[i] = bdx.guitarTimer[i].pair;

                pianoOriginal[i] = new PianoChord(BitConverter.GetBytes(bdx.pianoOrig[i]));
            }
            pianoMapIndexRootPair = bdx.pianoPair;

            guitarOriginal = bdx.guitarOrig.Select(x => new GuitarChord(x)).ToArray();

            karaokeLyrics = bdx.karaokeLyrics;
            karaokeTimer = bdx.karaokeTimer;
            //chordChanges = bdx.ChordChanges;

            for (int i = 0; i < 255; i++)
            {
                chordTimer.AddTime(bdx.chordTimer[i].time);
                chordIndex[i] = bdx.chordTimer[i].value;
            }

            //keySignature = bdx.KeySig[8].Stuff;
            //PianoChord.highestNote = bdx.PianoHighestNote;
            //if (PianoChord.highestNote == 0) { PianoChord.highestNote = 70; }
            //pianoVoicingStyle = bdx.PianoVoicingStyle;
            //pianoAvailability = bdx.PianoAvailability;
        }

        //public Karaoke GetKaraoke()
        //{
        //    return new Karaoke(karaokeLyrics, karaokeTimer);
        //}
    }
}