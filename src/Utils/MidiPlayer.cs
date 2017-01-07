using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Degausser
{
    public class MidiPlayer : INotifyPropertyChanged
    {
        [DllImport("winmm.dll")]
        static extern uint midiOutOpen(out IntPtr lphMidiOut, uint uDeviceID, int dwCallback, int dwInstance, uint dwFlags);
        [DllImport("winmm.dll")]
        static extern uint midiOutShortMsg(IntPtr hMidiOut, int dwMsg);
        [DllImport("winmm.dll")]
        static extern uint midiOutClose(IntPtr hMidiOut);
        [DllImport("winmm.dll")]
        static extern int midiOutSetVolume(IntPtr uDeviceID, int dwVolume);
        [DllImport("winmm.dll")]
        static extern int midiOutGetVolume(IntPtr uDeviceID, out int lpdwVolume);

        const int MAX_CHANNELS = 10;
        const int MAX_TICKS = 7200;

        enum MessageType
        {
            StopAllNotes = 0x7B,
            NoteOff = 0x80,
            NoteOn = 0x90,
            Polyphonic = 0xA0,
            ControlChange = 0xB0,
            ProgramChange = 0xC0,
            Aftertouch = 0xD0,
            PitchWheel = 0xE0
        };

        static IntPtr MidiOut;
        public static MidiPlayer Instance { get; private set; }

        MidiData midiData = new MidiData(new short[0]);
        Thread thread;

        public abstract class Chord
        {
            public virtual byte[] Notes { get; protected set; }
        }

        struct MidiMessage
        {
            int message;

            public MidiMessage(MessageType msgType, byte channel, byte note, byte volume)
            {
                message = (volume << 16) + (note << 8) + (int)msgType + channel;
            }

            public void Execute() => midiOutShortMsg(MidiOut, message);
        }

        public class MidiChannel
        {
            byte lastNote;
            Chord lastChord;
            MidiMessage[,] message = new MidiMessage[MAX_TICKS, 12];
            int[] msgCount = new int[MAX_TICKS];
            byte channel;
            bool isActive = true;

            public MidiChannel(byte channel)
            {
                this.channel = channel == 9 ? (byte)10 : channel; // channel 9 reserved for percussion
            }

            void AddMessage(int position, MidiMessage message)
            {
                this.message[position, msgCount[position]++] = message;
            }

            public void AddNote(int position, byte note, byte volume)
            {
                if (note == 0) return;
                lastNote = note;
                AddMessage(position, new MidiMessage(MessageType.NoteOn, channel, note, volume));
            }

            void ReleaseNote(int position, byte note)
            {
                if (note == 0) return;
                AddMessage(position, new MidiMessage(MessageType.NoteOff, channel, note, 0));
            }

            public void ReleaseNote(int position)
            {
                ReleaseNote(position, lastNote);
                lastNote = 0;
            }

            public void AddChord(int position, Chord chord, byte volume)
            {
                lastChord = chord;
                foreach (byte note in chord.Notes)
                {
                    AddNote(position, note, volume);
                }
            }

            public void ReleaseChord(int position)
            {
                if (lastChord == null) return;
                foreach (byte note in lastChord.Notes)
                {
                    ReleaseNote(position, note);
                }
            }

            public void AddDrum(int position, byte drum, byte volume)
            {
                if (drum == 0) return;
                AddMessage(position, new MidiMessage(MessageType.NoteOn, 9, drum, volume));
            }

            public void ExecuteMessages(int position)
            {
                for (int i = 0; i < msgCount[position]; i++)
                {
                    message[position, i].Execute();
                }
            }

            public byte InstrumentMidi { get; set; }

            public string InstrumentName { get; set; }

            public void Silent()
            {
                new MidiMessage(MessageType.ControlChange, channel, (byte)MessageType.StopAllNotes, 0).Execute();
            }

            public void Initialize()
            {
                new MidiMessage(MessageType.ProgramChange, channel, InstrumentMidi, 0).Execute();
            }

            public bool IsActive
            {
                get
                {
                    return isActive;
                }
                set
                {
                    isActive = value;
                    if (!isActive) Silent();
                }
            }
        }

        public class MidiData
        {
            public MidiData(short[] tempo)
            {
                Tempo = tempo;
            }

            public MidiChannel this[int index] => Channels[index];
            public short[] Tempo { get; }
            public List<MidiChannel> Channels { get; } = Enumerable.Range(0, MAX_CHANNELS).Select(i => new MidiChannel((byte)i)).ToList();

        }

        // Opens a MIDI output device for playback
        public MidiPlayer()
        {
            Instance = this;
            midiOutOpen(out MidiOut, 0,0, 0, 0);
            for (int i = 0; i < MAX_CHANNELS; i++)
            {
                midiData.Channels[i].InstrumentName = $"Instrument {i}";
            }
        }

        // Closes the MIDI output device
        public void Close()
        {
            midiOutClose(MidiOut);
        }

        // Gets and sets the MIDI volume
        public ushort Volume
        {
            get
            {
                int volume;
                midiOutGetVolume(MidiOut, out volume);
                return (ushort)(volume & 0xFFFF);
            }
            set
            {
                ushort volume = value;
                midiOutSetVolume(MidiOut, (volume << 16) | volume);
            }
        }

        public double TempoModifier { get; set; }

        public bool IsPlaying { get; set; }

        public void SilentAll()
        {
            foreach (var ch in midiData.Channels)
            {
                ch.Silent();
            }
        }

        int position;
        public int Position
        {
            get
            {
                return position;
            }
            set
            {
                position = value;
                SilentAll();
            }
        }

        public int Length => midiData?.Tempo.Length ?? 0;

        public void Play(MidiData midiData)
        {
            IsPlaying = false;
            while (thread != null && thread.IsAlive) ;
            this.midiData = midiData;
            Position = 0;
            Play();
            NotifyPropertyChanged(nameof(Length));
            NotifyPropertyChanged(nameof(Channels));
        }

        public void Play()
        {
            if (IsPlaying) return;
            IsPlaying = true;
            thread = new Thread(MainLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            thread.Start();
        }

        public List<MidiChannel> Channels => midiData.Channels;

        // The main loop that runs the MIDI playback
        void MainLoop()
        {
            foreach (var c in midiData.Channels)
            {
                c.Initialize();
            }

            long nextTick = 0;
            while (IsPlaying && position < Length)
            {
                long currentTick = DateTime.Now.Ticks;
                if (currentTick >= nextTick)
                {
                    nextTick = currentTick + TimeSpan.TicksPerMinute / (long)(midiData.Tempo[position] * 12);
                    foreach (var c in midiData.Channels)
                    {
                        if (c.IsActive)
                        {
                            c.ExecuteMessages(position);
                        }
                    }
                    NotifyPropertyChanged(nameof(Position));
                    position++;
                }
                Thread.Sleep(1);
            }
            SilentAll();
            IsPlaying = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged(string info) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));


    }
}