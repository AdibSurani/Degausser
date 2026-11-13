using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Degausser
{
    public static class Language
    {
        public static string[] romaji =
        {
            "-a", "a", "-i", "i", "-u", "u", "-e", "e", "-o", "o",
            "ka", "ga", "ki", "gi", "ku", "gu", "ke", "ge", "ko", "go",
            "sa", "za", "shi", "ji", "su", "zu", "se", "ze", "so", "zo",
            "ta", "da", "chi", "dji", "っ", "tsu", "dzu", "te", "de", "to", "do",
            "na", "ni", "nu", "ne", "no",
            "ha", "ba", "pa", "hi", "bi", "pi", "fu", "bu", "pu", "he", "be", "pe", "ho", "bo", "po",
            "ma", "mi", "mu", "me", "mo",
            "-ya", "ya", "-yu", "yu", "-yo", "yo",
            "ra", "ri", "ru", "re", "ro",
            "-wa", "wa", "wi", "we", "wo", "n", "vu"
        };

        public static string Romanize(string str)
        {
            var sb = new StringBuilder();
            foreach (char itChar in str)
            {
                char c = itChar;
                bool katakana = false;
                if (c >= 'ァ' && c <= 'ヴ')
                {
                    katakana = true;
                    c = (char)(c - 'ァ' + 'ぁ');
                }
                if (c >= 'ぁ' && c <= 'ゔ')
                {
                    foreach (var c2 in romaji[c - 'ぁ'])
                    {
                        if (c2 == '-')
                        {
                            if (sb.Length > 0) sb.Length--;
                        }
                        else
                        {
                            sb.Append(katakana ? char.ToUpperInvariant(c2) : c2);
                        }
                    }
                }
                else if (c == 'ー' && sb.Length > 0)
                {
                    sb.Append(sb[sb.Length - 1]);
                }
                else
                {
                    sb.Append(c);
                }
            }

            // second pass to convert 'っ'
            for (int i = 0; i < sb.Length - 1; i++)
            {
                if (sb[i] == 'っ') sb[i] = sb[i + 1];
            }
            return sb.ToString();
        }

        // Band Brothers DX uses its own one/two-byte Japanese character table;
        // these strings are not UTF-16. This decoder is preserved from the
        // historical BDX tooling and is also used by the 2026 BDX->BBP reference converter.
        private const string BdxSingleByteCode = "　　　　　　をぁぃぅぇぉゃゅょっ～あいうえおかきくけこさしすせそ 。「」、・ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワン゙゚たちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわん★©";
        private const string BdxMultiByteCode = "ガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポヴ\0\0\0\0\0\0がぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽ\0\0\0\0\0\0\0×÷≠→↓←↑※〒♭♪±℃○●◎△▲▽▼□■◇◆☆★°∞∴…™®♂♀αβγπΣ√ゞ制作投稿大中小";
        public const char BdxZeroWidthSpace = '\u200B';

        public static string GetBDXJPString(byte[] bdxJPBytes, bool lyricsMode = false)
            => bdxJPBytes == null ? string.Empty : GetBDXJPString((ReadOnlySpan<byte>)bdxJPBytes, lyricsMode);

        public static string GetBDXJPString(ReadOnlySpan<byte> bytes, bool lyricsMode = false)
        {
            if (bytes.IsEmpty) return string.Empty;

            var result = new StringBuilder(bytes.Length);
            bool multibyte = false;
            foreach (byte b in bytes)
            {
                if (multibyte)
                {
                    result.Append(b < BdxMultiByteCode.Length ? BdxMultiByteCode[b] : '�');
                    multibyte = false;
                }
                else if (b == 0)
                {
                    if (lyricsMode) result.Append('\n');
                    else break;
                }
                else if (b < 0x20 && !lyricsMode)
                {
                    // The DX title editor leaves 0x10 in an empty title line (139 corpus files) or after
                    // the text (3); DX draws nothing for it. Corpus-inferred.
                }
                else if (b < 0x80)
                {
                    result.Append((char)b);
                }
                else if (b == 0x80)
                {
                    multibyte = true;
                    if (lyricsMode) result.Append(BdxZeroWidthSpace);
                }
                else if (b is 0xDE or 0xDF)
                {
                    if (result.Length > 0)
                        result[result.Length - 1] = (char)(result[result.Length - 1] + b - 0xDD);
                    if (lyricsMode) result.Append(BdxZeroWidthSpace);
                }
                else
                {
                    int index = b - 0x80;
                    result.Append(index < BdxSingleByteCode.Length ? BdxSingleByteCode[index] : '�');
                }
            }
            return result.ToString();
        }

        public static string Simplify(this string str) => TextFolding.Compatibility(str.ToLowerInvariant());
        public static char Simplify(this char c) => c.ToString().Simplify()[0];

        /// <summary>Japanese punctuation a romanisation may omit; kana never may.</summary>
        private static bool IsLyricPunctuation(char c)
            => char.IsPunctuation(c) || (char.IsSymbol(c) && c != 'ー');

        public static List<string> Romanize(this string jap, string eng) => Romanize(jap, eng, null);

        /// <summary>
        /// Like <see cref="Romanize(string, string)"/>, also returning where each chunk starts in
        /// <paramref name="eng"/>. Chunks are contiguous: chunk k covers eng[Starts[k]..Starts[k+1]],
        /// including override parentheses and the whitespace after it.
        /// </summary>
        public static (List<string> Chunks, List<int> Starts) RomanizeWithOffsets(this string jap, string eng)
        {
            var starts = new List<int>();
            return (Romanize(jap, eng, starts), starts);
        }

        private static List<string> Romanize(string jap, string eng, List<int>? starts)
        {
            int index = 0;
            var lst = new List<string>();  // Mutable for Add in loop
            void Begin(int at) { lst.Add(""); starts?.Add(at); }

            Func<bool, char?, bool>? takeOne = null;
            takeOne = (isFirstLetter, c) =>
            {
                if (index >= eng.Length) return false;
                if (eng[index].Simplify() != (c ?? eng[index]).Simplify()) return false;
                if (isFirstLetter) Begin(index);
                lst[lst.Count - 1] += eng[index++];
                while (index != eng.Length && char.IsWhiteSpace(eng[index]))
                {
                    _ = takeOne!(false, null);
                }
                return true;
            };

            foreach (var itChar in jap.Where(c => !char.IsWhiteSpace(c)))
            {
                bool success = true;
                var c = itChar.Simplify();
                if (index >= eng.Length)
                {
                    // Trailing punctuation may have no romaji counterpart; it keeps its timer.
                    if (IsLyricPunctuation(c)) { Begin(index); continue; }
                    throw new InvalidDataException("Mismatch: English lyric ended early.");
                }
                var peek = eng[index].Simplify();
                if (peek == c) success = takeOne!(true, null);
                else if (peek == '(')
                {
                    // Special case: text in parentheses is an explicit manual
                    // pronunciation/alignment override for this one source unit.
                    int open = index++;
                    int end = eng.IndexOf(')', index);
                    if (end < 0)
                        throw new FormatException($"Unclosed scorefix alignment override at lyric offset {open}.");
                    Begin(open);
                    lst[^1] = eng[index..end];
                    index = end + 1;
                    while (index < eng.Length && char.IsWhiteSpace(eng[index]))
                        _ = takeOne!(false, null);
                }
                else if (char.IsPunctuation(peek) && char.IsPunctuation(c)) success = takeOne!(true, null);
                else if (c is '~' or '〜') success = takeOne!(true, null); // ～ lengthens a vowel, like ー
                else if (IsLyricPunctuation(c))
                {
                    Begin(index); // e.g. ’’ or 「」 the romaji leaves out; one chunk per timed character
                    continue;
                }
                else if ("ーっッ".Contains(c)) success = takeOne!(true, null);
                else if (c is 'は' or 'ハ' && peek == 'w') success = takeOne!(true, 'w') && takeOne!(false, 'a');
                else
                {
                    // take kana
                    if (c >= 'ァ' && c <= 'ヴ') c = (char)(c - 'ァ' + 'ぁ'); // convert to hiragana

                    success = false;
                    if (c >= 'ぁ' && c <= 'ゔ')
                    {
                        foreach (var newc in romaji[c - 'ぁ'])
                        {
                            success |= takeOne!(!success, newc);
                        }
                    }
                    // Hepburn separates syllabic ん from a following vowel or y: kon'ya.
                    if (success && c == 'ん' && index < eng.Length && eng[index] is '\'' or '’')
                        _ = takeOne!(false, null);
                }

                if (!success)
                {
                    string actual = index < eng.Length ? eng[index].ToString() : "<end>";
                    throw new InvalidDataException($"Mismatch at target lyric offset {index} ('{actual}' vs. source '{itChar}').");
                }
            }

            if (index != eng.Length)
                throw new InvalidDataException($"Mismatch at target lyric offset {index}: extra unmatched text begins with '{eng[index]}'.");

            return lst;
        }
    }
}
