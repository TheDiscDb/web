using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Services;

namespace TheDiscDb.UnitTests.Services;

public class DiscScanComparisonStoreTests
{
    [Test]
    public async Task Serialize_RoundTripsComparisonHistoryAsReadableJson()
    {
        var file = new DiscScanComparisonFile
        {
            Comparisons =
            [
                new DiscScanComparisonRecord
                {
                    ComparedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    Status = DiscLogManifestComparisonStatus.Mismatch,
                    Format = "blu-ray",
                    ProducerVersion = "1.0",
                    LogTitleCount = 5,
                    ManifestTitleCount = 6,
                    MatchedTitleCount = 5,
                    Differences = new DiscLogManifestDifferences
                    {
                        Error = "boom",
                        IdentifyExperience = IdentifyExperienceComparison.NotComparable("boom")
                    }
                }
            ]
        };

        string json = DiscScanComparisonStore.Serialize(file);
        var roundTripped = DiscScanComparisonStore.Deserialize(json);

        await Assert.That(json).Contains("\"status\": \"Mismatch\"");
        await Assert.That(json).Contains("\"error\": \"boom\"");
        await Assert.That(roundTripped).IsNotNull();
        await Assert.That(roundTripped!.Comparisons.Count).IsEqualTo(1);
        await Assert.That(roundTripped.Comparisons[0].Status).IsEqualTo(DiscLogManifestComparisonStatus.Mismatch);
        await Assert.That(roundTripped.Comparisons[0].ManifestTitleCount).IsEqualTo(6);
        await Assert.That(roundTripped.Comparisons[0].Differences.Error).IsEqualTo("boom");
        await Assert.That(roundTripped.Comparisons[0].Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.NotComparable);
    }

    [Test]
    public async Task Deserialize_ReturnsNullForInvalidJson()
    {
        await Assert.That(DiscScanComparisonStore.Deserialize("not json")).IsNull();
        await Assert.That(DiscScanComparisonStore.Deserialize("")).IsNull();
    }

    [Test]
    public async Task Serialize_PreservesGradedCountsAndOptionalHistoricalScores()
    {
        var disc = new MakeMkv.DiscInfo();
        disc.Titles.Add(new MakeMkv.Title { Playlist = "1" });
        var measured = IdentifyExperienceComparer.Compare(disc, disc);
        var file = new DiscScanComparisonFile
        {
            Comparisons =
            [
                new DiscScanComparisonRecord { Differences = new() { IdentifyExperience = measured } },
                new DiscScanComparisonRecord { Differences = new() { IdentifyExperience = measured with { Similarity = null } } }
            ]
        };
        var restored = DiscScanComparisonStore.Deserialize(DiscScanComparisonStore.Serialize(file))!;
        var score = restored.Comparisons[0].Differences.IdentifyExperience!.Similarity!;
        await Assert.That(score.Overall).IsEqualTo(100d);
        await Assert.That(score.RowPresentation.Matching).IsEqualTo(7L);
        await Assert.That(score.Ordering.Total).IsEqualTo(1L);
        await Assert.That(restored.Comparisons[1].Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.Match);
        await Assert.That(restored.Comparisons[1].Differences.IdentifyExperience!.Similarity).IsNull();
    }
}
