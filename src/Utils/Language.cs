using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Degausser.Utils
{
    static class Language
    {
        const string SingleByteCode = "　　　　　　をぁぃぅぇぉゃゅょっ～あいうえおかきくけこさしすせそ 。「」、・ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワン゙゚たちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわん★©";
        const string MultiByteCode = "ガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポヴ\0\0\0\0\0\0がぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽ\0\0\0\0\0\0\0×÷≠→↓←↑※〒♭♪±℃○●◎△▲▽▼□■◇◆☆★°∞∴…™®♂♀αβγπΣ√ゞ制作投稿大中小";
        const string KanaToRomaji = "-a.a..-i.i..-u.u..-e.e..-o.o..ka.ga.ki.gi.ku.gu.ke.ge.ko.go.sa.za.shiji.su.zu.se.ze.so.zo.ta.da.chiji.~..tsudzute.de.to.do.na.ni.nu.ne.no.ha.ba.pa.hi.bi.pi.fu.bu.pu.he.be.pe.ho.bo.po.ma.mi.mu.me.mo.-yaya.-yuyu.-yoyo.ra.ri.ru.re.ro.-wawa.wi.we.wo.n..vu.";
        public const char zeroWidthSpace = (char)0x200b;

        public static string GetBDXJPString(IEnumerable<byte> bytes, bool lyricsMode = false)
        {
            var result = new StringBuilder();
            bool multibyte = false;
            foreach (var b in bytes)
            {
                if (multibyte)
                {
                    result.Append(MultiByteCode[b]);
                    multibyte = false;
                }
                else if (b == 0)
                {
                    if (lyricsMode)
                    {
                        result.Append('\n');
                    }
                    else
                    {
                        break;
                    }
                }
                else if (b < 0x80)
                {
                    result.Append((char)b);
                }
                else if (b == 0x80)
                {
                    multibyte = true;
                    if (lyricsMode)
                    {
                        result.Append(zeroWidthSpace);
                    }
                }
                else if (b == 0xde || b == 0xdf)
                {
                    if (result.Length > 0)
                    {
                        result[result.Length - 1] += (char)(b - 0xdd);
                    }
                    if (lyricsMode)
                    {
                        result.Append(zeroWidthSpace);
                    }
                }
                else // (b > 0x80)
                {
                    result.Append(SingleByteCode[b - 0x80]);
                }
            }
            return result.ToString();
        }

        public static string Jap2Eng(string JapString)
        {
            var EngString = new StringBuilder();
            foreach (char IteratedCharacter in JapString)
            {
                char c = IteratedCharacter;
                bool katakana = false;
                if (c >= 'ァ' && c <= 'ヴ')
                {
                    katakana = true;
                    c = (char)(c - 'ァ' + 'ぁ');
                }
                else if (c >= 'ぁ' && c <= 'ゔ')
                {
                    for (int i = 0; i < 3; i++)
                    {
                        char newc = KanaToRomaji[3 * (c - 'ぁ') + i];
                        switch (newc)
                        {
                            case '.':
                                break;
                            case '-':
                                if (EngString.Length > 0)
                                {
                                    EngString.Length--;
                                }
                                break;
                            case '~':
                                EngString.Append(c);
                                break;
                            default:
                                EngString.Append(katakana ? char.ToUpper(newc) : newc);
                                break;
                        }
                    }
                }
                else
                {
                    EngString.Append(c);
                }
            }
            for (int i = 0; i < EngString.Length; i++)
            {
                switch (EngString[i])
                {
                    case 'ー':
                        if (i > 0)
                        {
                            EngString[i] = EngString[i - 1];
                        }
                        break;
                    case 'っ':
                    case 'ッ':
                        if (i < EngString.Length - 1)
                        {
                            EngString[i] = EngString[i + 1];
                        }
                        break;
                }
            }
            return EngString.ToString();
        }

        public static string IOFriendly(this string str)
        {
            int length = str.Length;
            char[] buffer = new char[length];
            for (int i = 0; i < length; i++)
            {
                char c = str[i], d;
                switch (c)
                {
                    case '/': d = '／'; break;
                    case '\\': d = '＼'; break;
                    case '?': d = '？'; break;
                    case '%': d = '％'; break;
                    case '*': d = '＊'; break;
                    case ':': d = '：'; break;
                    case '|': d = '｜'; break;
                    case '"': d = '＂'; break;
                    case '<': d = '＜'; break;
                    case '>': d = '＞'; break;
                    default: d = c < 32 ? ' ' : c; break;
                }
                buffer[i] = d;
            }
            return new string(buffer);
        }

        static readonly string[] kanaGroups =
        {
            "アカサタナハマヤラワ",
            "イキシチニヒミリ",
            "ウクスツヌフムユル",
            "エケセテネヘメレ",
            "オコソトノホモヨロ"
        };

        public static string Simplify(this string str)
        {
            var sb = new StringBuilder();
            foreach (var fullchar in str)
            {
                if (fullchar == 'ー')
                {
                    sb.Append(kanaGroups.Single(g => g.Contains(sb[sb.Length - 1]))[0]);
                }
                else if ("•…※、。「」〒".Contains(fullchar))
                {
                    sb.Append(fullchar);
                }
                else if (fullchar < 0x7F || (fullchar > 0x3040 && fullchar < 0x30FB) || fullchar > 0xFF00)
                {
                    var c = fullchar.ToString().ToLower().Normalize(NormalizationForm.FormKD)[0];
                    if (c >= 'ぁ' && c <= 'ゔ')
                    {
                        c += (char)0x60; // hiragana -> katakana
                    }
                    if ("ァィゥェォッャュョヮ".Contains(c))
                    {
                        c++; // small kana -> big kana
                    }
                    else if (c == 'ヵ')
                    {
                        c = 'カ';
                    }
                    else if (c == 'ヶ')
                    {
                        c = 'ケ';
                    }
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
