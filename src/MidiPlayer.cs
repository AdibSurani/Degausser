using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace Degausser
{
    public class MidiPlayer : INotifyPropertyChanged
    {
        [DllImport("winmm.dll")]
        static extern uint midiOutOpen(out int lphMidiOut, int uDeviceID, int dwCallback, int dwInstance, uint dwFlags);
        [DllImport("winmm.dll")]
        static extern uint midiOutShortMsg(int hMidiOut, int dwMsg);
        [DllImport("winmm.dll")]
        static extern uint midiOutClose(int hMidiOut);
        [DllImport("winmm.dll")]
        static extern int midiOutSetVolume(int uDeviceID, int dwVolume);
        [DllImport("winmm.dll")]
        static extern int midiOutGetVolume(int uDeviceID, out int lpdwVolume);

        const int MAX_CHANNELS = 10;
        const int MAX_TICKS = 7200;

        public enum MidiPlayerState
        {
            Paused, Playing
        };

        enum MessageType
        {
            NoteOff = 0x80,
            NoteOn = 0x90,
            Polyphonic = 0xA0,
            ControlChange = 0xB0,
            ProgramChange = 0xC0,
            Aftertouch = 0xD0,
            PitchWheel = 0xE0
        };
        const byte StopAllNotes = 0x7B;

        static int MidiOut;
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
                new MidiMessage(MessageType.ControlChange, channel, StopAllNotes, 0).Execute();
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
                for (int i = 0; i < Channels.Length; i++)
                {
                    Channels[i] = new MidiChannel((byte)i);
                }
            }

            public MidiChannel this[int index] => Channels[index];
            public short[] Tempo { get; }
            public int Length => Tempo.Length;
            public MidiChannel[] Channels { get; } = new MidiChannel[MAX_CHANNELS];

        }

        // Opens a MIDI output device for playback
        public MidiPlayer()
        {
            Instance = this;
            midiOutOpen(out MidiOut, 0, 0, 0, 0);
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

        public MidiPlayerState State { get; set; }

        public void SilentAll()
        {
            for (int i = 0; i < 8; i++)
            {
                midiData.Channels[i].Silent();
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

        public int Length
        {
            get
            {
                if (midiData == null) return 0;
                return midiData.Length;
            }
        }

        public void Play(MidiData midiData)
        {
            State = MidiPlayerState.Paused;
            while (thread != null && thread.IsAlive) ;
            this.midiData = midiData;
            Position = 0;
            TempoModifier = 0;
            Play();
            NotifyPropertyChanged(nameof(Length));
            NotifyPropertyChanged(nameof(Channels));
            NotifyPropertyChanged(nameof(TempoModifier));
        }

        public void Play()
        {
            switch (State)
            {
                case MidiPlayerState.Playing:
                    return;
                case MidiPlayerState.Paused:
                    State = MidiPlayerState.Playing;
                    thread = new Thread(MainLoop);
                    thread.IsBackground = true;
                    thread.Priority = ThreadPriority.AboveNormal;
                    thread.Start();
                    break;
            }
        }

        public MidiChannel[] Channels => midiData.Channels;

        // The main loop that runs the MIDI playback
        void MainLoop()
        {
            foreach (var c in midiData.Channels)
            {
                c.Initialize();
            }

            long nextTick = 0;
            while (State == MidiPlayerState.Playing && position < Length)
            {
                long currentTick = DateTime.Now.Ticks;
                if (currentTick >= nextTick)
                {
                    double actualTempoModifier = Math.Pow(2, TempoModifier);
                    nextTick = currentTick + TimeSpan.TicksPerMinute / (long)(midiData.Tempo[position] * 12 * actualTempoModifier);
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
            State = MidiPlayerState.Paused;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }


    }
}