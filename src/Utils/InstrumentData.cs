using System;
using System.Text;
using System.Collections.Generic;
using Degausser.Properties;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Xml.Linq;

namespace Degausser
{
    static class InstrumentData
    {
        public class Instrument
        {
            public string Name;
            public byte Midi;
            public List<byte> DrumNotes;
        }

        public static Dictionary<int, Instrument> Instruments = new Dictionary<int, Instrument>();

        static InstrumentData()
        {
            ParseInstrumentXml();
        }

        static void ParseInstrumentXml()
        {
            var root = XDocument.Parse(Resources.Instruments).Root;
            foreach (var x in root.Elements("Instrument"))
            {
                int id = int.Parse(x.Attribute("ID").Value);
                var midi = x.Attribute("Midi").Value;
                var name = x.Attribute("Name").Value;
                switch (x.Attribute("Type").Value)
                {
                    case "NUL":
                    case "SCL":
                        Instruments[id] = new Instrument
                        {
                            Name = name,
                            Midi = byte.Parse(midi)
                        };
                        break;
                    case "DRM":
                        Instruments[id] = new Instrument
                        {
                            Name = name,
                            DrumNotes = midi.Split(',').Select(byte.Parse).ToList()
                        };
                        break;
                }
            }

            foreach (var x in root.Elements("InstrumentRange"))
            {
                var id = x.Attribute("ID").Value.Split('-').Select(int.Parse).ToList();
                var offset = int.Parse(x.Attribute("Offset").Value);
                for (int i = id[0]; i <= id[1]; i++)
                {
                    Instruments[i] = Instruments[i + offset];
                }
            }
        }

        public static readonly byte[] PianoChords = Resources.PianoChords;
        public static readonly byte[] GuitarChords = Resources.GuitarChords;
        public static readonly byte[] MajorScale = { 0, 2, 4, 5, 7, 9, 11 }; // CDEFGAB
        public static readonly int[] GuitarTuning = { 64, 59, 55, 50, 45, 40 }; // EBGDAE
    }
}
