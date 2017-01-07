using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static Degausser.Logging;

namespace Degausser
{
    public partial class MainWindow : Window
    {
        void SetTestDirectory()
        {
            Directory.SetCurrentDirectory(@"C:\Users\Adib\Downloads\bbp_9399\1-74\");
        }

        void VariousBinTests()
        {
            var mv = File.ReadAllBytes(@"C:\Users\Adib\Desktop\DAIGASSO\somegzs\mvmgr.bin").ToStruct<MvMgr>();
            var jb = File.ReadAllBytes(@"C:\Users\Adib\Desktop\DAIGASSO\somegzs\jbmgr.bin").ToStruct<JbMgr>();
            var jb2 = File.ReadAllBytes(@"C:\Users\Adib\Desktop\DAIGASSO\somegzs\jbmgr2.bin").ToStruct<JbMgr2>();
            var sg = File.ReadAllBytes(@"C:\Users\Adib\Desktop\DAIGASSO\somegzs\singermgr.bin").ToStruct<SingerMgr>();
            var sg2 = File.ReadAllBytes(@"C:\Users\Adib\Desktop\DAIGASSO\somegzs\singermgr2.bin").ToStruct<SingerMgr2>();
            var msgs = "Hi! I'm Aoki Lapis.\nI'll give my all\nwhen I sing!|Who's vocals are\nthe most heavenly?\nCamui Gackpo!|I'm Megpoid's GUMI.\nI'm happy to sing\nyour songs.|I'm ZOLA PROJECT's\nKYO!\nThanks for\nyour support!|I'm ZOLA PROJECT's\nYUU.\nIt's nice to meet you!|I'm ZOLA PROJECT's\nWIL.\nI'll sing the cool songs.|I'm galaco.\nNice to meet you!\nI'd love to sing.|Lovely to meet you!\nI'm MAYU! Please\nsupport me!|I'm ARSLOID!\nI'll do my best\nsinging your songs.|I'm Sachiko!\nNice to meet you!\nI'd love to sing.".Split('|');
            var mvs = "RADIO FISH\nPERFECT HUMAN;RADIO FISH\nPERFECT HUMAN|Splatoon\nInk Me Up;Splatoon\nInk Me Up|Splatoon\nCity of Color;Splatoon\nCity of Color|LYLA BOOPS\nChiisana Furusato;LYLA BOOPS\nChiisana Furusato|Mario Kart 8 Medley;Mario Kart 8 Medley|WarioWare, Inc.\nAshley's Theme;WarioWare, Inc.\nAshley's Theme|Splatoon\nSquid Sisters Song;Splatoon\nSquid Sisters Song|Wagakki Band\nHanabi;Wagakki Band\nHanabi|GUMI\nJust Kidding;GUMI\nJust Kidding|GUMI\nCrouching Start;GUMI\nCrouching Start|Golden Bomber\nLaura no Kizudarake;Golden Bomber\nLaura no Kizudarake|Dempagumi.inc\nDenden Passion;Dempagumi.inc\nDenden Passion|Silent Siren\nBANG!BANG!BANG!;Silent Siren\nBANG!BANG!BANG!|Yanchan Gakuen Ongakubo\nWanchan Kokuhaku;Yanchan Gakuen Ongakubo\n          Team Pockin\n    Wanchan Kokuhaku|Yanchan Gakuen Ongakubo\nKoi Hanabi;Yanchan Gakuen Ongakubo\n          Team Tingting\n             Koi Hanabi|Funassyi\nBugi Bugi Funassyi ♪;                 Funassyi\n         Bugi Bugi Funassyi ♪\nFunassyi Official Theme Song 2|Nintendo Princess\nBandbros P × Flipnote 3D;Collaboration w/ Flipnote Studio\n　Producer\n　　　タカヒロさん\n　Flipnote animation\n　　　ゆーいさん\n　　　ゆお+うごぉぉぉさん\n　　　おたまじゃくしさん|Luigi also does his best!\nBandbros P × Flipnote 3D;Collaboration w/ Flipnote Studio\n　Producer\n　　　かるとんさん\n　Flipnote animation\n　　　もりさん\n　　　キーファさん\n　　　あおらいんさん|Funassyi\nFuna Funa Funassyi ♪;                 Funassyi\n         Funa Funa Funassyi ♪\n～Funassyi Official Theme Song～|Doburock\nShin Moshikashite Dakedo;            Doburock\nShin Moshikashite Dakedo|Doburock\nMoshikashite Dakedo;        Doburock\nMoshikashite Dakedo|GUMI\nGoing My Way!;      GUMI\n\nGoing My Way!|Fudanjuku\nDansou Revolution;      Fudanjuku\n\nDansou Revolution|Fudanjuku\nJinsei Wahaha!;   Fudanjuku\n\nJinsei Wahaha!|Negicco\nFestival de Aimashou;          Negicco\n\nFestival de Aimashou|Negicco\nAnata to Pop With You!;           Negicco\n\nAnata to Pop With You!|Yumemiru Adolescence\nMawaru Sekai;Yumemiru Adolescence\n\n        Mawaru Sekai|Gero\nBELOVED×SURVIVAL;　　　　Gero\n\nBELOVED×SURVIVAL|Wagakki Band\nRoku-chou-nen to Ichiya...;  Wagakki Band\n\nRoku-chou-nen to\nIchiya Monogatari|Wagakki Band\nSenbonzakura;Wagakki Band\nSenbonzakura|Soyo Fuku Hibi to Kokoro\nYohou ～Playable Ver～;Aoki Lapis\n\nSoyo Fuku Hibi to Kokoro Yohou\n～Playable Version～|～Outgrow～;GERO ～Outgrow～|The Legend of Zelda: A\nLink Between Worlds;April issue of Nintendo Dream\nThe Legend of Zelda: A Link\nBetween Worlds Medley\n\nThis arrangement is the work of\nGrand Prix recipient んばさん.|Super Mario 3D\nWorld Medley;April issue of Nintendo Dream\nSuper Mario 3D World Medley\n\nThis arrangement is the work of\nGrand Prix recipient みんとさん.|Super Mario 64 Mix;Super Mario Bros.\nand Super Mario 64|Memeshikute;Golden Bomber\n\n  Memeshikute|Dance My Generation;    Golden Bomber\n\nDance My Generation|101 Kaime no Noroi;   Golden Bomber\n\n101 Kaime no Noroi|Fire Emblem Awakening\nConquest (Ablaze);Fire Emblem Awakening\nConquest (Ablaze)\n\nHope or destruction: what's your\ndestiny? You befriend Prince\nChrom and many other people\nin an epic battle for the future.|The Legend of Zelda\nField Medley DX;The Legend of Zelda:\n　Ocarina of Time\n　Twilight Princess\n　Majora's Mask\n　Wind Waker".Split('|');
            sg.items[12].artist = "Camui";
            sg2.items[12].item.artist = "Camui";
            for (int i = 0; i < 40; i++)
            {
                //sg.items[i + 11].greeting = msgs[i];
                //sg2.items[i + 11].item.greeting = msgs[i];
                var spl = mvs[i].Split(';');
                mv.items[i].title = spl[0];
                mv.items[i].description = spl[1];
            }
            //File.WriteAllBytes(@"C:\Users\Adib\Desktop\DAIGASSO\somegzs\singermgr_fixed.bin", sg.StructToArray());
            //File.WriteAllBytes(@"C:\Users\Adib\Desktop\DAIGASSO\somegzs\singermgr2_fixed.bin", sg2.StructToArray());
            //File.WriteAllBytes(@"C:\Users\Adib\Desktop\DAIGASSO\somegzs\mvmgr_fixed.bin", mv.StructToArray());
        }

        void KaraokeTest()
        {
            Gak gak, gak2;
            string id, eng;
            short[] newt;
            List<string> lst;

            id = "80010000";
            gak = BBPRecord.FromPack($"C:/dbbp/unver/patch/Gak3/{id}/pack").gak;
            gak2 = BBPRecord.FromPack($"C:/dbbp/patch/Gak3/{id}/pack").gak;
            eng = "Always shining brightly|at midnight not to lose|my way in the life.|Never let you go, boy.|`Cause you’re my|STARLIGHT STARLIGHT.||Always shining brightly|at midnight not to lose|my way in the life.|Never let you go, boy.|`Cause you’re my|STARLIGHT STARLIGHT.||Konna koto ga|okiru nante|Koi nante GEEMU dato|omotteta|Shiroku jichuu|Kimi ni muchuu|Honto daisuki|Hen ni narisou oh||S-T-A-R-L-I-G-H-T|Doko ni itemo|kagayaiteru|S-T-A-R-L-I-G-H-T|Kimi wa watashi no|Starlight in the sky||Dokode demo|itsudemo|Nanka dokidoki suru no|My Starlight|Sekinin totte kureru?|Soba ni kite gyutto shite|Zutto icha icha shiyou|My Starlight|Kiechawanai de ne||Dokode demo|itsudemo|Nanka dokidoki suru no|My Starlight|Sekinin totte kureru?|Soba ni kite gyutto shite|Zutto icha icha shiyou|My Starlight|Kiechawanai de ne||Always shining brightly|at midnight not to lose|my way in the life.|Never let you go, boy.|`Cause you’re my|STARLIGHT STARLIGHT.||Always shining brightly|at midnight not to lose|my way in the life.|Never let you go, boy.|`Cause you’re my|STARLIGHT STARLIGHT.|";
            eng = eng.Replace("|", "\n");
            lst = gak.karaokeLyrics.Romanize(eng);
            newt = Karaoke.Retime(lst, gak.karaokeTimer);
            Log(gak2.karaokeLyrics == string.Concat(lst));
            Log(gak2.karaokeTimer.SequenceEqual(newt));

            id = "80010001";
            gak = BBPRecord.FromPack($"C:/dbbp/unver/patch/Gak3/{id}/pack").gak;
            gak2 = BBPRecord.FromPack($"C:/dbbp/patch/Gak3/{id}/pack").gak;
            eng = "Katari aitai|Ano toki megutta|Kimochi no tomadoi|Koe ni shinakatta||Ima kuyande mo|Kyou omoi shirasetai|Ima nayande|Kyou oshietai||Feel me|See me|Say me|Gonna talk to you|Nee oboeteru|Itsuka dekaketa toki|Anata to watashi dake|Uiteta ne||Itsugoro doko de|Dare to nan no tame ni|Soko ni ita no ka nante|Dou demo ii||Hokorashige ni|Harebare to shiteta|Futari no kimochi ga|Tsuuji atte iru to||Kanjite ita|Tonikaku umaku wa|Ienai keredo|Nanika mitasareteta||Ano shunkan wa|Kitto kitto ja naku|Machigaina kuzettai|Modoranai||Aikawarazu kyou mo|Heibonde nanimo kamo|Kowakunai hibi wo|Negatteru||Woo...|Uh...|Woo...|Uh...|Uh...|Uh...|Uh...|Uh...||Naze nan darou|Anata to ita toki|Yowaku naranai|Dokoka jiman shiteru||Ima kuyande mo|Kyou omoi shirasetai|Ima nayande|Kyou oshietai||";
            eng = eng.Replace("|", "\n");
            lst = gak.karaokeLyrics.Romanize(eng);
            newt = Karaoke.Retime(lst, gak.karaokeTimer);
            Log(gak2.karaokeLyrics == string.Concat(lst));
            Log(gak2.karaokeTimer.SequenceEqual(newt));

            id = "80010002";
            gak = BBPRecord.FromPack($"C:/dbbp/unver/patch/Gak3/{id}/pack").gak;
            gak2 = BBPRecord.FromPack($"C:/dbbp/patch/Gak3/{id}/pack").gak;
            eng = "Hyaku tsuume no tegami|ga kitara|Ayaui kimi ga kiete|shimai sou de|Omowazu boku wa|koe wo ageta|Kimi ga kidzuku you ni||Wakannai|Dou surya ii nanka|Dakedo, doushite mo|houtte okenai|Konna baka de kurai|yatsu no kotoba|Hitsuyou ga aru nara||Ikirarenu yowasa wa|boku ni tayoreba ii|Itsumademo mimimoto|de utau yo|Maru de \"noroi\" de ii|Kimi no shimobe de ii|Furisosogu sono itami|migaware||Naze nan darou|Kimi ga tegakari|mitai de|Tama ni miseta kitanai|kokoro wo|Boku wa utsukushiku|omou||Ikiru to wa nanika? to|kotae no nai toi ga|Kimi to ai tokesou na|ki ga suru no|Maru de noroi de ii|Kimi no shimobe de ii|Furisosogu sono itami|migaware||Ikiro yo to|nando demo itte yaru|Kono noroi|kimi wo sukue|";
            eng = eng.Replace("|", "\n");
            lst = gak.karaokeLyrics.Romanize(eng);
            newt = Karaoke.Retime(lst, gak.karaokeTimer);
            Log(gak2.karaokeLyrics == string.Concat(lst));
            Log(gak2.karaokeTimer.SequenceEqual(newt));

            id = "80010003";
            gak = BBPRecord.FromPack($"C:/dbbp/unver/patch/Gak3/{id}/pack").gak;
            gak2 = BBPRecord.FromPack($"C:/dbbp/patch/Gak3/{id}/pack").gak;
            eng = "Fukanzen na kanjou|Zenbu ROKETTO ni|tsumekonde|Uchiageta|Tai kiken mo koeta|Koko de nara|Sukoshi sunao ni nareru|ka na||Daijina mono wa|Minai kuse shite|Mawari no me wa|ki ni narun da yo|Haribote mitaku|Moroku ayaui|Sonna no jibun demo|Wakatterun da yo||Kotoba ni dekinai|kimochi wa|Uso nan janai katte|Kowakute|Utagatte kyou mo|mae ni|Susumenai de irunda||Fukanzen na kanjou|Zenbu ROKETTO ni|tsumekonde|Uchiageta|Dare no te mo|todokanai kurai|Tooku|Mikansei na kanjou|Zenbu ROKETTO ni|tsumekonde|Uchiageta|Tai kiken mo koeta|Koko de nara|Sukoshi sunao ni nareru|ka na||Haruka kanata he|Boku no yowamushi|ROKETTO|";
            eng = eng.Replace("|", "\n");
            lst = gak.karaokeLyrics.Romanize(eng);
            newt = Karaoke.Retime(lst, gak.karaokeTimer);
            Log(gak2.karaokeLyrics == string.Concat(lst));
            Log(gak2.karaokeTimer.SequenceEqual(newt));
        }
    }
}
