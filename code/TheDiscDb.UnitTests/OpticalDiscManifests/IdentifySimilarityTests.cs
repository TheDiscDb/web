using System.Text.Json;
using MakeMkv;
using TheDiscDb.Contributions.OpticalDiscManifest;

namespace TheDiscDb.UnitTests.OpticalDiscManifests;

public class IdentifySimilarityTests
{
    [Test]
    public async Task Perfect_UsesApprovedWeightsWithoutRounding()
    {
        var score = Score(Disc("1", "2"), Disc("1", "2"));
        await Assert.That(score.Overall).IsEqualTo(100d);
        await Assert.That(score.ChoiceCoverage.Matching).IsEqualTo(2L);
        await Assert.That(score.RowPresentation.Total).IsEqualTo(14L);
        await Assert.That(score.Labeling.Total).IsEqualTo(14L);
        await Assert.That(score.Ordering.Total).IsEqualTo(1L);
        var changed = Disc("2", "1");
        changed.Titles[0].DisplaySize = "other";
        changed.Titles[0].Segments.Add(new Segment { Type = "Audio", Name = "DTS" });
        var result = Score(Disc("1", "2"), changed);
        double expected = .4 * result.ChoiceCoverage.Percent!.Value
            + .25 * result.RowPresentation.Percent!.Value + .15 * result.Ordering.Percent!.Value
            + .2 * result.Labeling.Percent!.Value;
        await Assert.That(result.Overall).IsEqualTo(expected);
    }

    [Test]
    public async Task Reorder_OnlyChangesOrdering_NotAlignedPresentationOrLabeling()
    {
        var result = IdentifyExperienceComparer.Compare(Disc("1", "2", "3"), Disc("3", "1", "2"));
        var score = result.Similarity!;
        await Assert.That(result.Status).IsEqualTo(IdentifyExperienceStatus.Mismatch);
        await Assert.That(result.MatchingRowCount).IsEqualTo(0);
        await Assert.That(score.RowPresentation.Percent).IsEqualTo(100d);
        await Assert.That(score.Labeling.Percent).IsEqualTo(100d);
        await Assert.That(score.Ordering.Matching).IsEqualTo(1L);
        await Assert.That(score.Ordering.Total).IsEqualTo(3L);
        await Assert.That(score.Overall).IsEqualTo(90d);
        await Assert.That(score.Differences.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrExtra_UnionDenominatorsPenalizeEveryComponent(bool extra)
    {
        var score = extra ? Score(Disc("1", "3"), Disc("1", "2", "3")) : Score(Disc("1", "2", "3"), Disc("1", "3"));
        await Assert.That(score.MatchedChoices).IsEqualTo(2);
        await Assert.That(score.MissingChoices.Count + score.ExtraChoices.Count).IsEqualTo(1);
        await Assert.That(score.ChoiceCoverage.Total).IsEqualTo(3L);
        await Assert.That(score.RowPresentation.Matching).IsEqualTo(14L);
        await Assert.That(score.RowPresentation.Total).IsEqualTo(21L);
        await Assert.That(score.Labeling.Matching).IsEqualTo(14L);
        await Assert.That(score.Labeling.Total).IsEqualTo(21L);
        await Assert.That(score.Ordering.Matching).IsEqualTo(1L);
        await Assert.That(score.Ordering.Total).IsEqualTo(3L);
        await Assert.That(score.Overall!.Value < 67).IsTrue();
    }

    [Test]
    public async Task OmittingBadChoice_DoesNotIncreaseAnyComponent()
    {
        var log = Disc("1", "2");
        var bad = Disc("1", "2");
        bad.Titles[1].Length = "bad";
        bad.Titles[1].Segments.Add(new Segment { Type = "Audio", Name = "DTS" });
        var before = Score(log, bad);
        var after = Score(log, Disc("1"));
        await Assert.That(after.Overall!.Value < before.Overall!.Value).IsTrue();
        await Assert.That(after.ChoiceCoverage.Percent!.Value <= before.ChoiceCoverage.Percent!.Value).IsTrue();
        await Assert.That(after.RowPresentation.Percent!.Value <= before.RowPresentation.Percent!.Value).IsTrue();
        await Assert.That(after.Labeling.Percent!.Value <= before.Labeling.Percent!.Value).IsTrue();
    }

    [Test]
    public async Task LabelText_OnlyChangesLabeling_NamesSeparateFromCounts()
    {
        var log = Disc("1");
        var scan = Disc("1");
        log.Titles[0].Segments.Add(new Segment { Type = "Audio", Name = "DTS" });
        scan.Titles[0].Segments.Add(new Segment { Type = "Audio", Name = "AC3" });
        var score = Score(log, scan);
        await Assert.That(score.RowPresentation.Percent).IsEqualTo(100d);
        await Assert.That(score.Labeling.Matching).IsEqualTo(6L);
        await Assert.That(score.Differences.Single().Field).IsEqualTo("AudioDialogNames");
        scan.Titles[0].Segments.Add(new Segment { Type = "Audio", Name = "DTS" });
        score = Score(log, scan);
        await Assert.That(score.Labeling.Matching).IsEqualTo(5L);
        await Assert.That(score.Differences.Any(d => d.Field == "AudioDialogCount")).IsTrue();
    }

    [Test]
    public async Task FullMapAndTooltip_AreOnePresentationConcept()
    {
        var scan = Disc("1");
        scan.Titles[0].SegmentMap = "1,2,3,4,5,6,7,8,9,10";
        var score = Score(Disc("1"), scan);
        await Assert.That(score.RowPresentation.Matching).IsEqualTo(6L);
        await Assert.That(score.Differences.Single().Field).IsEqualTo("SegmentMap");
    }

    [Test]
    public async Task CommentColumnVisibility_IsCountedForEachUnionRow()
    {
        var scan = Disc("1", "2");
        scan.Titles[0].JavaComment = " ";
        var score = Score(Disc("1", "2"), scan);
        await Assert.That(score.RowPresentation.Matching).IsEqualTo(11L);
        await Assert.That(score.Differences.Count(d => d.Field == "CommentColumnVisible")).IsEqualTo(2);
    }

    [Test]
    public async Task Duplicates_PairByOccurrence_NotBestMetadataMatch()
    {
        var log = Disc("1", "1");
        var scan = Disc("1", "1");
        log.Titles[0].Length = "first";
        log.Titles[1].Length = "second";
        scan.Titles[0].Length = "second";
        scan.Titles[1].Length = "first";
        var score = Score(log, scan);
        await Assert.That(score.MatchedChoices).IsEqualTo(2);
        await Assert.That(score.Differences.Count(d => d.Field == "Length")).IsEqualTo(2);
        await Assert.That(score.Ordering.Percent).IsEqualTo(100d);
        score = Score(log, Disc("1"));
        await Assert.That(score.MatchedChoices).IsEqualTo(1);
        await Assert.That(score.MissingChoices.Count).IsEqualTo(1);
        await Assert.That(score.ChoiceCoverage.Percent).IsEqualTo(50d);
    }

    [Test]
    public async Task Identity_DoesNotAliasPlaylistsOrClipSets_ButDvdPaddingIsPresentationOnly()
    {
        var log = Disc("00001.mpls");
        var scan = Disc("00002.mpls");
        var different = Score(log, scan);
        await Assert.That(different.MatchedChoices).IsEqualTo(0);
        await Assert.That(different.Overall).IsEqualTo(0d);
        var padded = Score(Disc("01"), Disc("1"));
        await Assert.That(padded.MatchedChoices).IsEqualTo(1);
        await Assert.That(padded.Differences.Single().Field).IsEqualTo("Playlist");
        await Assert.That(padded.Ordering.Percent).IsEqualTo(100d);
    }

    [Test]
    public async Task UnknownSources_AreNotGuessedOrRewarded()
    {
        var score = Score(Disc(""), Disc(""));
        await Assert.That(score.MatchedChoices).IsEqualTo(0);
        await Assert.That(score.Overall).IsEqualTo(0d);
        await Assert.That(IdentifyExperienceComparer.Compare(new(), Disc("1")).Similarity).IsNull();
        await Assert.That(IdentifyExperienceComparison.NotComparable("different disc or error").Similarity).IsNull();
    }

    [Test]
    public async Task HistoricalAndFutureScores_AreNotMeasured_AndJsonRoundTrips()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var old = JsonSerializer.Deserialize<IdentifyExperienceComparison>(
            """{"version":1,"status":"Match","logRowCount":1,"manifestRowCount":1,"matchingRowCount":1,"differences":[]}""", options)!;
        await Assert.That(old.Similarity).IsNull();
        var current = IdentifyExperienceComparer.Compare(Disc("1"), Disc("1"));
        var restored = JsonSerializer.Deserialize<IdentifyExperienceComparison>(JsonSerializer.Serialize(current, options), options)!;
        await Assert.That(restored.Similarity!.Overall).IsEqualTo(100d);
        var future = current with { Similarity = current.Similarity! with { Version = 99 } };
        await Assert.That(future.Similarity!.Overall).IsNull();
        var aggregate = IdentifySimilarityAggregate.Create([old, current, future, IdentifyExperienceComparison.NotComparable("error"), null]);
        await Assert.That(aggregate.Measured).IsEqualTo(1);
        await Assert.That(aggregate.NotMeasured).IsEqualTo(3);
        await Assert.That(aggregate.NotComparable).IsEqualTo(1);
        await Assert.That(aggregate.Overall).IsEqualTo(100d);
    }

    [Test]
    public async Task Aggregate_IsMeanPerDisc_NotPooledRows()
    {
        var perfect = IdentifyExperienceComparer.Compare(Disc("1"), Disc("1"));
        var none = IdentifyExperienceComparer.Compare(Disc("1", "2", "3"), Disc("4", "5", "6"));
        var stats = IdentifySimilarityAggregate.Create([perfect, none]);
        await Assert.That(stats.Overall).IsEqualTo(50d);
        await Assert.That(stats.ChoiceCoverage).IsEqualTo(50d);
        await Assert.That(stats.MissingChoices).IsEqualTo(3);
        await Assert.That(IdentifySimilarityAggregate.Create([]).Overall).IsNull();
    }

    [Test]
    public async Task Ordering_AgreesWithPairEnumerationAcrossDeterministicPermutations()
    {
        var random = new Random(17);
        string[] sources = Enumerable.Range(1, 30).Select(i => i.ToString()).ToArray();
        for (int run = 0; run < 20; run++)
        {
            string[] permutation = sources.OrderBy(_ => random.Next()).ToArray();
            int[] indices = sources.Select(s => Array.IndexOf(permutation, s)).ToArray();
            long expected = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                for (int j = i + 1; j < indices.Length; j++)
                {
                    if (indices[i] < indices[j]) expected++;
                }
            }
            await Assert.That(Score(Disc(sources), Disc(permutation)).Ordering.Matching).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Ordering_LargeChoiceListsUseLongPairCountsWithoutPairMatrix()
    {
        string[] sources = Enumerable.Range(1, 70_000).Select(i => i.ToString()).ToArray();
        var score = IdentifySimilarityComparer.Compare(Disc(sources), Disc(sources))!;
        await Assert.That(score.Ordering.Total).IsEqualTo(2_449_965_000L);
        await Assert.That(score.Overall).IsEqualTo(100d);
    }

    private static IdentifySimilarityScore Score(DiscInfo log, DiscInfo scan)
        => IdentifyExperienceComparer.Compare(log, scan).Similarity!;

    private static DiscInfo Disc(params string[] sources)
    {
        var disc = new DiscInfo();
        foreach (string source in sources)
        {
            disc.Titles.Add(new Title { Playlist = source, Length = "1:00", DisplaySize = "1 MB", ChapterCount = 2, SegmentMap = "1" });
        }
        return disc;
    }
}
