using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Degausser
{
    static class Language
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
                            sb.Append(katakana ? char.ToUpper(c2) : c2);
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

        public static string Simplify(this string str) => str.ToLower().Normalize(NormalizationForm.FormKC);
        public static char Simplify(this char c) => c.ToString().Simplify()[0];

        public static List<string> Romanize(this string jap, string eng)
        {
            int index = 0;
            var lst = new List<string>();

            Func<bool, char?, bool> TakeOne = null;
            TakeOne = (IsFirstLetter, c) =>
            {
                if (eng[index].Simplify() != (c ?? eng[index]).Simplify()) return false;
                if (IsFirstLetter) lst.Add("");
                lst[lst.Count - 1] += eng[index++];
                while (index != eng.Length && char.IsWhiteSpace(eng[index]))
                {
                    TakeOne(false, null);
                }
                return true;
            };

            foreach (var itChar in jap.Where(c => !char.IsWhiteSpace(c)))
            {
                bool success = true;
                var c = itChar.Simplify();
                var peek = eng[index].Simplify();
                if (peek == c) success = TakeOne(true, null);
                else if (peek == '(')
                {
                    // special case of taking things between parentheses
                    var end = eng.IndexOf(')', index++);
                    lst.Add(eng.Substring(index, end - index));
                    index = end + 1;
                }
                else if (char.IsPunctuation(peek) && char.IsPunctuation(c)) success = TakeOne(true, null);
                else if ("ーっッ".Contains(c)) success = TakeOne(true, null);
                else if (c == 'は' && peek == 'w') success = TakeOne(true, 'w') && TakeOne(false, 'a');
                else
                {
                    // take kana
                    if (c >= 'ァ' && c <= 'ヴ') c = (char)(c - 'ァ' + 'ぁ'); // convert to hiragana

                    success = false;
                    if (c >= 'ぁ' && c <= 'ゔ')
                    {
                        foreach (var newc in romaji[c - 'ぁ'])
                        {
                            success |= TakeOne(!success, newc);
                        }
                    }
                }

                if (!success) throw new Exception($"Mismatch at lyric offset {index} ('{eng[index]}' vs. '{itChar}')");
            }
            return lst;
        }
    }
}
