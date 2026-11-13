using System.Buffers.Binary;

namespace Degausser.Core.Tests;

public class MoviePackTests
{
    /// <summary>A minimal 0x00010003 pack: a gzip score, a raw payload after it and a tail.</summary>
    private static byte[] BuildPlayablePack(byte[] expanded, byte[] other, byte[] tail)
    {
        byte[] score = expanded.Compress();
        int scoreOffset = 8 + 7 * 16;
        int otherOffset = scoreOffset + ((score.Length + 3) & ~3);
        byte[] pack = new byte[otherOffset + other.Length + tail.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(pack, PlayableVideoPack.Version);
        BinaryPrimitives.WriteInt32LittleEndian(pack.AsSpan(4), 7);
        void Descriptor(int i, int mode, int size, int offset, int stored)
        {
            int at = 8 + i * 16;
            BinaryPrimitives.WriteInt32LittleEndian(pack.AsSpan(at), mode);
            BinaryPrimitives.WriteInt32LittleEndian(pack.AsSpan(at + 4), size);
            BinaryPrimitives.WriteInt32LittleEndian(pack.AsSpan(at + 8), offset);
            BinaryPrimitives.WriteInt32LittleEndian(pack.AsSpan(at + 12), stored);
        }
        Descriptor(0, 1, expanded.Length, scoreOffset, score.Length);
        Descriptor(4, 0, other.Length, otherOffset, other.Length);
        score.CopyTo(pack, scoreOffset);
        other.CopyTo(pack, otherOffset);
        tail.CopyTo(pack, otherOffset + other.Length);
        return pack;
    }

    private static byte[] ExpandedScore(string title)
    {
        byte[] expanded = new byte[ExpandedGakLayout.BinarySize];
        BinaryPrimitives.WriteUInt16LittleEndian(expanded, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(expanded.AsSpan(2), 2);
        System.Text.Encoding.Unicode.GetBytes(title).CopyTo(expanded, 0x10);
        return expanded;
    }

    [Fact]
    public void PlayableVideoPackTextEditKeepsTheWrapperAndOtherPayloads()
    {
        byte[] other = Enumerable.Range(0, 301).Select(i => (byte)(i * 7)).ToArray();
        byte[] tail = [9, 8, 7, 6, 5];
        byte[] pack = BuildPlayablePack(ExpandedScore("ニホンゴ"), other, tail);

        BBPRecord record = BBPRecord.FromPack(pack);
        Assert.Equal("ニホンゴ", record.gak.title);
        record.gak.title = "English Title\nSecond line";
        record.gak.karaokeLyrics = "ro-ma-ji\nlyrics";
        record.gak.karaokeTimer[0] = unchecked((short)0x8001);

        byte[] edited = PlayableVideoPack.WithScoreText(pack, record.gak);

        Assert.True(PlayableVideoPack.Is(edited));
        BBPRecord reloaded = BBPRecord.FromPack(edited);
        Assert.Equal("English Title\nSecond line", reloaded.gak.title);
        Assert.StartsWith("ro-ma-ji\nlyrics", reloaded.gak.karaokeLyrics);
        Assert.Equal(unchecked((short)0x8001), reloaded.gak.karaokeTimer[0]);

        int otherOffset = BinaryPrimitives.ReadInt32LittleEndian(edited.AsSpan(8 + 4 * 16 + 8));
        Assert.Equal(0, otherOffset % 4);
        Assert.Equal(other, edited.AsSpan(otherOffset, other.Length).ToArray());
        Assert.Equal(tail, edited.AsSpan(otherOffset + other.Length).ToArray());
    }

    [FixtureFact("DEGAUSSER_MV_EXTDATA")]
    public void InstalledMoviesAreTranslatedTogetherWithTheirPacks()
    {
        string source = FixtureFactAttribute.GetPath("DEGAUSSER_MV_EXTDATA");
        string copy = Path.Combine(Path.GetTempPath(), "degausser-mv-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Everything except the multi-gigabyte movie payloads.
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                if (relative.StartsWith(Path.Combine("mv", "data")) || relative.StartsWith("voc") || relative.StartsWith("singer"))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, relative))!);
                File.Copy(file, Path.Combine(copy, relative));
            }

            var manifest = ContentTranslation.ReadBundledEnglish();
            var workspace = new ExtDataWorkspace(Path.Combine(copy, "jb", "mgr.bin"));
            Assert.Equal(40, workspace.TranslateInstalledMovies(manifest).Count);
            Assert.Empty(workspace.TranslateInstalledMovies(manifest));

            foreach (var movie in new ExtDataWorkspace(Path.Combine(copy, "jb", "mgr.bin")).ReadMovies())
            {
                BBPRecord record = workspace.LoadMovieSong(movie);
                Assert.Equal(movie.JbItem.item.title, record.gak.title.Replace("\n", string.Empty));
                Assert.DoesNotContain(record.gak.title, c => c is >= '぀' and <= '鿿');
            }
        }
        finally
        {
            if (Directory.Exists(copy)) Directory.Delete(copy, recursive: true);
        }
    }
}
