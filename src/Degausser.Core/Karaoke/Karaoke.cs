namespace Degausser
{
    public class Karaoke
    {
        const int MaxSyllableLength = 48;
        const int TimerMask = 0x3FFF;
        const int ActiveFlag = 0x8000;
        const int SeparatorFlag = 0x4000;
        const int ActiveSeparatorFlags = ActiveFlag | SeparatorFlag;
        readonly List<int> timer = [];

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
            if (IsEnabled = bbp.HasKaraoke)
            {
                Lyrics = bbp.karaokeLyrics;
                timer = [..bbp.karaokeTimer.Select(t => t & TimerMask)];
                int beatsPerMeasure = bbp.timeSignature is 3 or 4 ? bbp.timeSignature : 4;
                maxTicks = Math.Max(0, bbp.linesPlus6 - 6) * beatsPerMeasure * 12;
            }
        }

        public int Position
        {
            set
            {
                int searchableCount = Math.Min(Lyrics.Length, timer.Count);
                if (!IsEnabled || searchableCount == 0)
                {
                    PositionStart = 0;
                    PositionCount = 0;
                    PositionFraction = 0;
                    return;
                }

                int left = timer.BinarySearch(0, searchableCount, value, null);
                if (left < 0) left = ~left - 1;

                int right = left + 1;
                while (left > 0 && timer[left - 1] == timer[left]) left--;
                while (left >= 0 && right < searchableCount && timer[right] == timer[left]) right++;

                int timeLower = left < 0 ? 0 : timer[left];
                int timeUpper = right >= searchableCount ? timeLower + MaxSyllableLength : timer[right];
                int timeDiff = Math.Min(maxTicks - timeLower, Math.Min(timeUpper - timeLower, MaxSyllableLength));
                timeDiff = Math.Max(1, timeDiff);
                double frac = Math.Clamp((value - timeLower) * 1.0 / timeDiff, 0, 1);

                if (left < 0 || value >= timeLower + MaxSyllableLength)
                {
                    left = right;
                    frac = 0;
                }

                PositionStart = Math.Clamp(left, 0, searchableCount);
                PositionCount = Math.Max(0, right - PositionStart);
                PositionFraction = frac;
            }
        }

        public static short[] Retime(List<string> lst, short[] orig)
        {
            // Historical compatibility overload. Without source characters it
            // cannot distinguish a real lyric anchor from native GAK whitespace
            // that happens to carry 0x8000. New code should use the source-aware
            // overload below.
            return Retime(lst.Zip(orig.Where(t => (t & 0xC000) == ActiveFlag), Tuple.Create));
        }

        public static short[] Retime(string sourceLyrics, string targetLyrics, short[] orig)
            => RetimeLyrics(sourceLyrics, targetLyrics, orig).Timer;

        public static (string Lyrics, short[] Timer) RetimeLyrics(string sourceLyrics, string targetLyrics, short[] orig)
        {
            ArgumentNullException.ThrowIfNull(sourceLyrics);
            ArgumentNullException.ThrowIfNull(targetLyrics);
            ArgumentNullException.ThrowIfNull(orig);

            List<string> chunks = sourceLyrics.Romanize(targetLyrics);
            return (string.Concat(chunks), Retime(sourceLyrics, chunks, orig));
        }

        public static short[] Retime(string sourceLyrics, List<string> chunks, short[] orig)
        {
            ArgumentNullException.ThrowIfNull(sourceLyrics);
            ArgumentNullException.ThrowIfNull(chunks);
            ArgumentNullException.ThrowIfNull(orig);

            int sourceCount = Math.Min(sourceLyrics.Length, orig.Length);
            var anchors = new List<short>(chunks.Count);
            for (int i = 0; i < sourceCount; i++)
            {
                char c = sourceLyrics[i];
                if (char.IsWhiteSpace(c))
                    continue; // Tentai Kansoku et al.: whitespace may legally carry 0x8000.

                short value = orig[i];
                if ((value & 0xC000) != ActiveFlag)
                    throw new InvalidDataException(
                        $"Source karaoke character {i} ('{c}') has no ordinary timing anchor (timer 0x{(ushort)value:X4}).");
                anchors.Add(value);
            }

            if (anchors.Count != chunks.Count)
                throw new InvalidDataException(
                    $"Karaoke alignment produced {chunks.Count} pronunciation chunks but found {anchors.Count} timed source characters.");

            return Retime(chunks.Zip(anchors, Tuple.Create));
        }

        public static short[] Retime(IEnumerable<Tuple<string, short>> src)
        {
            var timer = new List<short>();
            string prevText = string.Empty;
            int prevTick = 0;

            foreach (var curr in src)
            {
                int currTick = curr.Item2 & TimerMask;
                AppendSegment(timer, prevText, prevTick, currTick);
                prevText = curr.Item1;
                prevTick = currTick;
            }

            AppendFinalSegment(timer, prevText, prevTick);
            if (timer.Count > 3000)
                timer.RemoveRange(3000, timer.Count - 3000);
            return timer.Concat(Enumerable.Repeat((short)TimerMask, 3000 - timer.Count)).ToArray();
        }

        private static void AppendSegment(List<short> timer, string text, int startTick, int nextTick)
        {
            if (string.IsNullOrEmpty(text)) return;

            int length = Math.Min(Math.Max(0, nextTick - startTick), MaxSyllableLength);
            int nonWhitespace = text.Count(c => !char.IsWhiteSpace(c));
            int index = 0;
            int lastTick = startTick & TimerMask;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    timer.Add(EncodeTimer(lastTick, separator: true));
                    continue;
                }

                int tick = nonWhitespace == 0
                    ? startTick
                    : startTick + length * index++ / nonWhitespace;
                lastTick = tick & TimerMask;
                timer.Add(EncodeTimer(lastTick, separator: false));
            }
        }

        private static void AppendFinalSegment(List<short> timer, string text, int tick)
        {
            int lastTick = tick & TimerMask;
            foreach (char c in text)
                timer.Add(EncodeTimer(lastTick, char.IsWhiteSpace(c)));
        }

        private static short EncodeTimer(int tick, bool separator)
            => unchecked((short)((tick & TimerMask) | (separator ? ActiveSeparatorFlags : ActiveFlag)));

        public static short[] Retime(string str, List<TimeValuePair> lst)
        {
            var timer = Enumerable.Repeat((short)TimerMask, 3000).ToArray();

            for (int i = 0; i < lst.Count - 1; i++)
            {
                var prev = lst[i];
                var next = lst[i + 1];

                // interpolate in between
                var interp = Enumerable.Range(prev.value, next.value - prev.value).Where(j => !char.IsWhiteSpace(str[j])).ToArray();
                var length = Math.Min(next.time - prev.time, MaxSyllableLength);
                for (int j = 0; j < interp.Length; j++)
                {
                    int tick = prev.time + length * j / interp.Length;
                    timer[interp[j]] = EncodeTimer(tick, separator: false);
                }
            }

            // Finally, mark whitespace/grouping entries using the most recent
            // ordinary timing anchor. Native P uses 0x8000 for ordinary entries
            // and adds 0x4000 for separators/spacing.
            int lastTick = 0;
            for (int i = 0; i < Math.Min(str.Length, timer.Length); i++)
            {
                if (char.IsWhiteSpace(str[i]))
                {
                    timer[i] = EncodeTimer(lastTick, separator: true);
                }
                else if ((timer[i] & TimerMask) != TimerMask)
                {
                    lastTick = timer[i] & TimerMask;
                }
            }

            return timer;
        }
    }
}
