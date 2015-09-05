using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Degausser
{
    public enum PlayType : byte { Standard, Drum, Guitar, Piano };

    [DebuggerDisplay("({index}, {rawRoot})")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct IndexRootPair
    {
        public byte index;
        public byte rawRoot;

        enum Accidental { Natural, Sharp, Flat };

        public byte Root
        {
            get
            {
                if (rawRoot == 0xFF) { return rawRoot; }
                byte note = InstrumentData.MajorScale[rawRoot & 0xF];
                switch ((Accidental)(rawRoot >> 4))
                {
                    case Accidental.Sharp:
                        return ++note;
                    case Accidental.Flat:
                        return --note;
                    case Accidental.Natural:
                    default:
                        return note;
                }
            }
        }

        public short Short => (short)((rawRoot << 8) | index);
    }

    [DebuggerDisplay("({time}, {value})")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TimeValuePair
    {
        public short time;
        public short value;
    }

    [DebuggerDisplay("({attack}, {decay}, {sustain}, {release})")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SoundEnvelope
    {
        public byte attack;
        public byte decay;
        public byte sustain;
        public byte release;
        public byte shape;
        public byte hold;
        public byte delay;
        public byte depth;
        public byte speed;
        public byte zero;
        public byte effectorType;
        public byte effectorValue;
    }
}
