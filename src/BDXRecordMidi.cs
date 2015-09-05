using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Degausser.Properties;

namespace Degausser
{
    partial class BDXRecord
    {
        class PianoChord : MidiPlayer.Chord
        {
            public PianoChord(byte[] notes)
            {
                Notes = notes;
            }

            public PianoChord(int mapIndex, byte rootNote, int highestNote, int minimum)
            {
                var notes = (from note in InstrumentData.PianoChords.Skip(4 * mapIndex).Take(4)
                             let newnote = ((note + rootNote) % 12 - highestNote) % 12 + highestNote
                             orderby newnote descending
                             select (byte)newnote)
                             .ToArray();
                //Array.Copy(InstrumentData.PianoChords, 4 * mapIndex, notes, 0, 4);
                //for (int i = 0; i < 4; i++)
                //{
                //    if (notes[i] > 0)
                //    {
                //        notes[i] = (byte)(((notes[i] + rootNote)  % 12 - highestNote) % 12 + highestNote);
                //    }
                //}
                if (notes[3] == 0 && minimum == 4)
                {
                    notes[3] = (byte)(notes.Take(3).Max() - 12);
                }
                Notes = notes;
            }
        }

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

        BDXChannel[] channel = new BDXChannel[8];
        MidiPlayer.MidiData midiData = null;

        public MidiPlayer.MidiData GetMidiData()
        {
            if (midiData == null)
            {
                ParseData();
                //SetUpMidi();
            }
            return midiData;
        }

        //public void SetUpMidi()
        //{
        //    midiData = new MidiPlayer.MidiData(lines * 48);
        //    for (int i = 0; i < lines * 48; i++)
        //    {
        //        midiData.Tempo[i] = tempoTimer.GetSnapshotAtTime(i).value;
        //    }

        //    byte[] volume = new byte[lines * 48];
        //    for (int i = 0; i < 8; i++)
        //    {
        //        midiData[i].InstrumentName = GetInstrumentName(i);
        //        //midiData[i].Instrument = Resourcez.Instrument[channel[i].instrument]; // uses 0-127
        //        midiData[i].Instrument = InstrumentData.Instruments[channel[i].instrument].Midi; // uses 0-127
        //        #region Set Volumes
        //        for (int j = 0; j < lines * 48; j++)
        //        {
        //            int vol = channel[i].changeVolume[channel[i].changeTimer[j]];
        //            vol *= channel[i].volume * (bdx.masterVolume + 64);
        //            volume[j] = (byte)Math.Min(127, vol >> 14);
        //        }
        //        #endregion
        //        int frame = 0;
        //        byte note = 0;
        //        bool IsThreeNotes = false;

        //        switch (channel[i].playType)
        //        {
        //            case PlayType.Standard:
        //                #region Normal Instrument Code
        //                for (int j = 0; j < lines * 16; j++)
        //                {
        //                    note = channel[i].notes[j];
        //                    if ((j & 3) == 0 && (IsThreeNotes = (note == 0xFF)))
        //                    {
        //                        continue;
        //                    }
        //                    else if ((note & 0x80) == 0)
        //                    {
        //                        midiData[i].ReleaseNote(frame);
        //                        if (note > 0)
        //                        {
        //                            midiData[i].AddNote(frame, note, volume[frame]);
        //                        }
        //                    }
        //                    frame += IsThreeNotes ? 4 : 3;
        //                }
        //                #endregion
        //                break;
        //            case PlayType.Drum:
        //                #region Drum Code
        //                for (int j = 0; j < lines * 4; j++)
        //                {
        //                    int[,] drumnote = new int[2, 4];
        //                    for (int k = 0; k < 4; k++)
        //                    {
        //                        drumnote[0, k] = channel[i].notes[4 * j + k] >> 4;
        //                        drumnote[1, k] = channel[i].notes[4 * j + k] & 0xF;
        //                    }
        //                    int[] delta = { 3, 3 }, cursor = { 0, 0 };
        //                    for (int k = 0; k < 1; k++)
        //                    {
        //                        if (drumnote[0, k] == 0xF)
        //                        {
        //                            delta[k] = 4;
        //                            cursor[k]++;
        //                        }
        //                    }
        //                    for (int k = 0; k < 12; k++)
        //                    {
        //                        for (int m = 0; m < 1; m++)
        //                        {
        //                            if (k % delta[m] == 0)
        //                            {
        //                                byte sound = InstrumentData.Instruments[channel[i].instrument].DrumNotes[drumnote[m, cursor[m]++]];
        //                                midiData[i].AddDrum(frame, sound, volume[frame]);
        //                            }
        //                        }
        //                        frame++;
        //                    }
        //                }
        //                #endregion
        //                break;
        //            case PlayType.Guitar:
        //                #region Guitar Chord Code
        //                RemapGuitarChords();
        //                for (int j = 0; j < lines * 16; j++)
        //                {
        //                    note = channel[i].notes[j];
        //                    if ((j & 3) == 0 && (IsThreeNotes = (note == 0xFF)))
        //                    {
        //                        continue;
        //                    }
        //                    else if ((note & 0x80) == 0)
        //                    {
        //                        midiData[i].ReleaseChord(frame);
        //                        if (note > 0)
        //                        {
        //                            midiData[i].AddChord(frame, remappedGuitar[guitarTimer[frame], note / 4], volume[frame]);
        //                        }
        //                    }
        //                    frame += IsThreeNotes ? 4 : 3;
        //                }
        //                #endregion
        //                break;
        //            case PlayType.Piano:
        //                #region Piano Chord Code
        //                RemapPianoChords();
        //                for (int j = 0; j < lines * 16; j++)
        //                {
        //                    note = channel[i].notes[j];
        //                    if ((j & 3) == 0 && (IsThreeNotes = (note == 0xFF)))
        //                    {
        //                        continue;
        //                    }
        //                    else if ((note & 0x80) == 0)
        //                    {
        //                        midiData[i].ReleaseChord(frame);
        //                        if (note > 0)
        //                        {
        //                            midiData[i].AddChord(frame, remappedChords[note - 1], volume[frame]);
        //                        }
        //                    }
        //                    frame += IsThreeNotes ? 4 : 3;
        //                }
        //                #endregion
        //                break;
        //        }
        //    }
        //}

        void RemapPianoChords()
        {
            int minimum = 3 + (bdx.pianoVoicingStyle / 3);
            int spread = bdx.pianoVoicingStyle % 3; // don't know what this does yet
            int highestNote = bdx.pianoHighestNote == 0 ? 70 : bdx.pianoHighestNote;
            for (int i = 0; i < 32; i++)
            {
                var pair = pianoMapIndexRootPair[i];
                if (pair.Root == 0xFF)
                {
                    if (pair.index == 0xFF) { break; }
                    remappedChords[i] = pianoOriginal[pair.index];
                }
                else
                {
                    remappedChords[i] = new PianoChord(pair.index, pair.Root, bdx.pianoHighestNote, minimum);
                }
            }
        }

        void RemapGuitarChords()
        {
            for (int i = 0; i < 32; i++)
            {
                for (int j = 0; j < 9; j++)
                {
                    if (guitarMapIndexRootPairs[i][j].Root == 0xFF)
                    {
                        if (guitarMapIndexRootPairs[i][j].index == 0xFF) { continue; }
                        remappedGuitar[i, j] = guitarOriginal[guitarMapIndexRootPairs[i][j].index];
                    }
                    else
                    {
                        remappedGuitar[i, j] = new GuitarChord(guitarMapIndexRootPairs[i][j].Root, guitarMapIndexRootPairs[i][j].index);
                    }
                }
            }
        }

        public string GetInstrumentName(int chanNumber)
        {
            int u = channel[chanNumber].instrument;
            if (u == 0) return "(Blank)";
            string clone = channel[chanNumber].cloneID > 0 ? " " + channel[chanNumber].cloneID : null;
            return $"{InstrumentData.Instruments[u].Name}{clone} ({channel[chanNumber].playType})";
        }
    }
}
