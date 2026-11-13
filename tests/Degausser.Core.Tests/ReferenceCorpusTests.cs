using Melanchall.DryWetMidi.Interaction;
using System.Security.Cryptography;
using System.Text.Json;

namespace Degausser.Core.Tests;

public class ReferenceCorpusTests
{
    [Fact]
    public void ReferenceManifestContainsRecoveredCorpus()
    {
        var manifest = LoadManifest();
        Assert.Equal(51, manifest.FixtureCount);
        Assert.Equal(51, manifest.Fixtures.Count);
        Assert.All(manifest.Fixtures, f =>
        {
            Assert.Equal(64, f.SourceSha256.Length);
            Assert.Equal(64, f.GakSha256.Length);
            Assert.Equal(64, f.ManagerItemSha256.Length);
        });
    }

    [FixtureFact("DEGAUSSER_BDX_FIXTURE_DIR")]
    public void ReferenceCorpusMatchesWhenFixtureDirectoryIsConfigured()
    {
        string root = FixtureFactAttribute.GetPath("DEGAUSSER_BDX_FIXTURE_DIR");

        var manifest = LoadManifest();
        var expected = manifest.Fixtures.ToDictionary(f => f.SourceSha256, StringComparer.OrdinalIgnoreCase);
        int matched = 0;

        foreach (string path in Directory.EnumerateFiles(root, "*.bdx", SearchOption.AllDirectories))
        {
            byte[] source = File.ReadAllBytes(path);
            string sourceHash = Convert.ToHexString(SHA256.HashData(source));
            if (!expected.TryGetValue(sourceHash, out var fixture))
                continue;

            var converted = BdxToBbpConverter.Convert(source, new BdxConversionOptions(
                PianoChordEncoding: BdxPianoChordEncoding.LegacyCompatibility,
                NormaliseForP: false));
            Assert.Equal(fixture.ExpectedTitleId, $"0x{converted.ManagerItem.titleID:X8}");

            // The historical Python oracle predates the recovered Nintendo
            // DX->P default for ChannelInfo +07/+08. Validate the corrected
            // values explicitly, then zero only +08 before comparing the rest
            // of the generated GAK byte-for-byte with that oracle.
            for (int channel = 0; channel < 8; channel++)
            {
                ChannelInfo info = converted.Gak.channelInfo[channel];
                Assert.Equal((byte)8, info.performanceParam07);
                Assert.Equal(
                    info.playType == PlayType.Drum ? (byte)0 : (byte)8,
                    info.performanceParam08);
            }

            byte[] oracleComparableGak = converted.RawGak.ToArray();
            for (int channel = 0; channel < 8; channel++)
                oracleComparableGak[0x020C + 48 * channel + 8] = 0;

            Assert.Equal(fixture.GakSha256, Convert.ToHexString(SHA256.HashData(oracleComparableGak)));
            // The oracle also predates JbFlags bit 17 (IsThreeFourMeter); validate
            // it against the GAK time signature, then clear it for the comparison.
            JbMgr.Item oracleComparableItem = converted.ManagerItem;
            Assert.Equal(converted.Gak.timeSignature == 3, oracleComparableItem.flags.IsThreeFourMeter);
            oracleComparableItem.flags.IsThreeFourMeter = false;
            // It also copied the title into titleSimple; Degausser lowercases it as Nintendo does.
            Assert.Equal(JbMgr.Item.SimpleTitle(oracleComparableItem.title), oracleComparableItem.titleSimple);
            oracleComparableItem.titleSimple = oracleComparableItem.title;
            Assert.Equal(fixture.ManagerItemSha256, Convert.ToHexString(SHA256.HashData(oracleComparableItem.ToBytes())));
            Assert.Equal(fixture.WarningCount, converted.Warnings.Count);

            // The recovered corpus contains many real Guitar migrations. If a
            // converted Guitar part has attack-like score bytes, ensure the MIDI
            // projection actually emits notes for that source part. This keeps
            // the test corpus external while exercising real chord palettes.
            var guitarChannels = converted.Gak.channelInfo
                .Select((info, index) => (info, index))
                .Where(x => x.info.playType == PlayType.Guitar)
                .Select(x => x.index)
                .ToArray();
            if (guitarChannels.Length > 0)
            {
                var record = BBPRecord.FromBdx(source);
                var midi = BbpToMidiConverter.Convert(record);
                foreach (int channelIndex in guitarChannels)
                {
                    bool hasAttackLikeByte = converted.Gak.channelNotes[channelIndex].notes
                        .Any(value => value is > 0 and < 0x80);
                    if (!hasAttackLikeByte)
                        continue;

                    byte midiChannel = midi.Channels[channelIndex].CanonicalChannel;
                    Assert.True(
                        midi.File.GetNotes().Any(note => (byte)note.Channel == midiChannel),
                        $"Guitar part {channelIndex + 1} contained attacks but emitted no MIDI notes.");
                }
            }

            matched++;
        }

        Assert.True(matched > 0, "Fixture directory was configured, but none of its BDX files matched the reference manifest.");
    }

    [FixtureFact("DEGAUSSER_BDX_FIXTURE_DIR")]
    public void EveryCorpusBdxConvertsRoundTripsAndProjectsToMidi()
    {
        string root = FixtureFactAttribute.GetPath("DEGAUSSER_BDX_FIXTURE_DIR");
        var failures = new List<string>();
        int count = 0;

        foreach (string path in Directory.EnumerateFiles(root, "*.bdx", SearchOption.AllDirectories))
        {
            count++;
            try
            {
                byte[] bbp = BBPRecord.FromBdx(File.ReadAllBytes(path), path).ToBbpBytes();
                var reread = BBPRecord.FromBbp(bbp);
                if (!reread.ToBbpBytes().AsSpan().SequenceEqual(bbp))
                    failures.Add($"{path}: BBP did not round-trip byte-for-byte");
                BbpToMidiConverter.Convert(reread);
            }
            catch (Exception exception)
            {
                failures.Add($"{path}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        Assert.True(count > 0, "Fixture directory was configured, but contains no BDX files.");
        Assert.True(failures.Count == 0, $"{failures.Count} of {count} BDX files failed:\n{string.Join('\n', failures.Take(20))}");
    }

    private static ReferenceManifest LoadManifest()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "bdx2bbp-reference-manifest.json");
        return JsonSerializer.Deserialize<ReferenceManifest>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Could not deserialize BDX reference manifest.");
    }

    private sealed record ReferenceManifest(string Reference, int FixtureCount, List<ReferenceFixture> Fixtures);
    private sealed record ReferenceFixture(
        string SourceSha256,
        string ExpectedTitleId,
        string GakSha256,
        string ManagerItemSha256,
        int WarningCount);
}
