using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Degausser
{
    class Karaoke
    {
        const int MaxSyllableLength = 96;
        readonly List<int> timer;

        public string Lyrics { get; }
        public int PositionStart { get; private set; }
        public int PositionCount { get; private set; }
        public double PositionFraction { get; private set; }
        public bool IsEnabled { get; }

        int maxTicks;

        public Karaoke()
        {
            Lyrics = "(no karaoke)";
            IsEnabled = false;
        }

        public Karaoke(BBPFormat bbp)
        {
            if (IsEnabled = bbp.karaokeTimer[0] != 0x3FFF)
            {
                Lyrics = new string(bbp.karaokeLyrics);
                timer = bbp.karaokeTimer.Select(t => t & 0x3FFF).ToList();
                maxTicks = (bbp.linesPlus6 - 6) * 48;
            }
            else
            {
                Lyrics = "(no karaoke)";
            }
        }

        public int Position
        {
            set
            {
                int left = timer.BinarySearch(0, Lyrics.Length, value, null);
                if (left < 0) left = ~left - 1;

                int right = left + 1;
                while (left > 0 && timer[left - 1] == timer[left]) left--;
                while (left >= 0 && right < timer.Count && timer[right] == timer[left]) right++;

                int timeLower = left < 0 ? 0 : timer[left];
                int timeUpper = right >= timer.Count ? timer[left] + MaxSyllableLength : timer[right];
                int timeDiff = Math.Min(maxTicks - timeLower, Math.Min(timeUpper - timeLower, MaxSyllableLength));
                double frac = (value - timeLower) * 1.0 / timeDiff;

                if (left < 0 || value >= timeLower + MaxSyllableLength)
                {
                    left = right;
                    frac = 0;
                }

                PositionStart = left;
                PositionCount = right - left;
                PositionFraction = frac;
            }
        }
    }
}
