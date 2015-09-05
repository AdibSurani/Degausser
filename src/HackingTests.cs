using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Degausser
{
    public partial class MainWindow : Window
    {
        void SetTestDirectory()
        {
            Directory.SetCurrentDirectory(@"C:\Users\Adib\Documents\Daigasso\Test Files");
        }

        void CompareSomeKnownSimilarFiles()
        {
            Directory.SetCurrentDirectory(@"C:\Users\Adib\Documents\Daigasso\Test Files");

            JbMgrFormat.JbMgrItem swordmgr, readymgr, harumgr;

            // piano?
            var sword1 = new BDXRecord("HEART OF SWORD (L@DX).bdx");
            var sword2 = new BBPRecord("ＨＥＡＲＴ　ＯＦ　ＳＷＯＲＤ (ＤＸいしょく＠Ｌ＠ＤＸ).bbp");
            var sword1bbp = BDX2BBP.Convert(sword1.bdx, out swordmgr);

            // guitar?
            var ready1 = new BDXRecord("Sobakasu (Rurouni Kenshin OP1).bdx");
            var ready2 = new BBPRecord("そばかす (ＤＸいしょく＠Ｎｉｎｔｅｎｄｏ).bbp");
            var ready1bbp = BDX2BBP.Convert(ready1.bdx, out readymgr);

            // no karaoke
            var haru1 = new BDXRecord("Haru no Umi.bdx");
            var haru2 = new BBPRecord("(無料)春の海 (ＤＸいしょく＠Ｎｉｎｔｅｎｄｏ).bbp");
            var haru1bbp = BDX2BBP.Convert(haru1.bdx, out harumgr);
        }

        void LookForSimilarSongs()
        {
            Func<byte[], int> hash = bytes =>
            {
                return bytes.Take(1000).Aggregate(0, (a, b) => a * 23 + b);
            };
            var bdxFiles = Directory.EnumerateFiles(@"C:\Users\Adib\Documents\Daigasso\BDX Mega Pack 2.4", "*.bdx", SearchOption.AllDirectories);
            var bbpFiles = Directory.EnumerateFiles(@"C:\Users\Adib\Documents\Daigasso\BBP Mega Pack 1.1", "*ＤＸいしょく*.bbp");
            var q = (from b1 in bdxFiles
                     let h1 = hash(new BDXRecord(b1).bdx.channelNotes[0].notes)
                     where h1 != 0
                     join b2 in bbpFiles on h1 equals hash(new BBPRecord(b2).bbp.channelNotes[0].notes)
                     select new { h1, b1, b2 });

            foreach (var x in q)
            {
                var bdx = new BDXRecord(x.b1).bdx;
                var bbp = new BBPRecord(x.b2).bbp;
                var s1 = bdx.channelNotes.Take(8).Select(y => hash(y.notes));
                var s2 = bbp.channelNotes.Take(8).Select(y => hash(y.notes));

                if (s1.Zip(s2, (h1, h2) => h1 == h2).Count(b => b) < 7) continue;

                if (bdx.channelInfo.Any(c => c.playType == PlayType.Guitar))
                {
                    Debug.Write("[GT] ");
                }
                else if (bdx.channelInfo.Any(c => c.playType == PlayType.Piano))
                {
                    Debug.Write("[KB] ");
                }
                else Debug.Write("[  ] ");

                Debug.WriteLine($"{Path.GetFileName(x.b1)}\t{Path.GetFileName(x.b2)}");
            }
        }

        void TestBDXAssertions()
        {
            var bdxFiles = Directory.EnumerateFiles(@"C:\Users\Adib\Documents\Daigasso\BDX Mega Pack 2.4", "*.bdx", SearchOption.AllDirectories);
            foreach (var path in bdxFiles)
            {
                try
                {
                    if (new FileInfo(path).Length != 32768) throw new Exception($"Wrong filesize {new FileInfo(path).Length}");
                    var bdx = new BDXRecord(path).bdx;
                    if (bdx.dateCreated != 0)
                    {
                        var date1 = BitConverter.GetBytes(bdx.dateCreated);
                        new DateTime(2000 + date1[0], date1[1], date1[2]);
                    }
                    if (bdx.dateModified != 0)
                    {
                        var date2 = BitConverter.GetBytes(bdx.dateModified);
                        new DateTime(2000 + date2[0], date2[1], date2[2]);
                    }
                    if (bdx.definitelyZero1 != 0) throw new Exception("not zero1");
                    if (bdx.definitelyZero2 != 0) throw new Exception("not zero2");
                    if (bdx.definitelyZero3 != 0) throw new Exception("not zero3");
                    if (bdx.definitelyZero4.Any(x => x != 0)) throw new Exception("not zero4");
                    if (bdx.channelInfo.Any(x => x.zero != 0)) throw new Exception("not chanzero");
                    if (bdx.chanInfo5.Any(x => x.stuff.Any(y => y.zero != 0))) throw new Exception("not chan5zero2");
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"{e.Message}) {path}");
                }
            }
        }
    }
}
