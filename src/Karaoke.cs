using System;
using System.Collections.Generic;
using System.Linq;

namespace Degausser
{
    class Karaoke
    {
        const int MaxSyllableLength = 48;
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

        public Karaoke(Gak bbp) : this()
        {
            if (IsEnabled = bbp.karaokeTimer[0] != 0x3FFF)
            {
                Lyrics = bbp.karaokeLyrics;
                timer = bbp.karaokeTimer.Select(t => t & 0x3FFF).ToList();
                maxTicks = (bbp.linesPlus6 - 6) * 48;
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

        public static short[] Retime(List<string> lst, short[] orig)
        {
            return Retime(lst.Zip(orig.Where(t => (t & 0xC000) == 0x8000), Tuple.Create));
        }

        public static short[] Retime(IEnumerable<Tuple<string, short>> src)
        {
            var timer = new List<short>();
            var prev = Tuple.Create("", (short)0);
            foreach (var curr in src)
            {
                var length = Math.Min(curr.Item2 - prev.Item2, MaxSyllableLength);
                var cnt = prev.Item1.Count(c => !char.IsWhiteSpace(c));
                int i = 0;
                timer.AddRange(prev.Item1.Select(c => (short)(char.IsWhiteSpace(c) ? timer.Last() | 0x4000 : prev.Item2 + length * i++ / cnt)));
                prev = curr;
            }
            foreach (var c in prev.Item1)
            {
                timer.Add(prev.Item2);
                if (char.IsWhiteSpace(c)) timer[timer.Count - 1] |= 0x4000;
            }
            return timer.Concat(Enumerable.Repeat((short)0x3FFF, 3000 - timer.Count)).ToArray();
        }

        public static short[] Retime(string str, List<TimeValuePair> lst)
        {
            var timer = Enumerable.Repeat((short)0x3FFF, 3000).ToArray();

            for (int i = 0; i < lst.Count - 1; i++)
            {
                var prev = lst[i];
                var next = i < lst.Count ? lst[i + 1]
                    : new TimeValuePair { time = prev.time, value = (short)str.Length }; // extrapolate (repeat) the last item

                // interpolate in between
                var interp = Enumerable.Range(prev.value, next.value - prev.value).Where(j => !char.IsWhiteSpace(str[j])).ToList();
                var length = Math.Min(next.time - prev.time, 48);
                for (int j = 0; j < interp.Count; j++)
                {
                    timer[interp[j]] = (short)(prev.time + length * j / interp.Count);
                }
            }

            // finally, fill in all the whitespace
            for (int i = 0, lasttime = 0x4000; i < str.Length; i++)
            {
                if (char.IsWhiteSpace(str[i])) timer[i] = (short)lasttime;
                lasttime = timer[i] | 0x4000;
            }

            return timer;
        }
    }
}
