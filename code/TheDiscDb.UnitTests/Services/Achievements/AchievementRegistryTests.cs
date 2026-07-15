namespace TheDiscDb.UnitTests.Services.Achievements;

using System.Linq;
using TheDiscDb.Services.Achievements;
using T = TheDiscDb.Services.Achievements.AchievementThresholds;

public class AchievementRegistryTests
{
    private static ContributorStats Stats(
        int published = 0, int series = 0, int approvedEdits = 0, int discIds = 0, int pending = 0,
        int formats = 0, int decades = 0, int genres = 0, int maxInGenre = 0, int boxsets = 0,
        int streakMonths = 0, bool comeback = false, bool firstTry = false) => new()
    {
        UserId = "user-1",
        ContributorName = "octocat",
        PublishedReleaseCount = published,
        SeriesReleaseCount = series,
        ApprovedEditSuggestionCount = approvedEdits,
        DiscIdContributionCount = discIds,
        PendingContributionCount = pending,
        DistinctFormatCount = formats,
        DistinctDecadeCount = decades,
        DistinctGenreCount = genres,
        MaxReleasesInSingleGenre = maxInGenre,
        ContributedBoxsetCount = boxsets,
        MaxConsecutiveContributionMonths = streakMonths,
        HadComebackGap = comeback,
        HasFirstTry = firstTry
    };

    [Test]
    public async Task AllKeysAreUnique()
    {
        var keys = AchievementRegistry.All.Select(d => d.Key).ToList();
        await Assert.That(keys.Distinct().Count()).IsEqualTo(keys.Count);
    }

    [Test]
    public async Task EveryDefinitionHasAnIcon()
    {
        foreach (var def in AchievementRegistry.All)
        {
            await Assert.That(string.IsNullOrWhiteSpace(def.Icon)).IsFalse();
        }
    }

    [Test]
    public async Task FirstContribution_EarnedAfterOneRelease()
    {
        var def = AchievementRegistry.Find("first-contribution")!;
        await Assert.That(def.Evaluate(Stats(published: 0)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(published: 1)).Earned).IsTrue();
    }

    [Test]
    public async Task ContributorBronze_ReportsProgressWhenNotEarned()
    {
        var def = AchievementRegistry.Find("contributor-bronze")!;
        var result = def.Evaluate(Stats(published: 3));
        await Assert.That(result.Earned).IsFalse();
        await Assert.That(result.Current).IsEqualTo(3);
        await Assert.That(result.Target).IsEqualTo(AchievementThresholds.ContributorBronze);
    }

    [Test]
    public async Task DiscIdDetectiveBronze_EarnedAtThreshold()
    {
        var def = AchievementRegistry.Find("disc-id-detective-bronze")!;
        await Assert.That(def.Evaluate(Stats(discIds: AchievementThresholds.DiscIdDetectiveBronze - 1)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(discIds: AchievementThresholds.DiscIdDetectiveBronze)).Earned).IsTrue();
    }

    [Test]
    public async Task FirstDiscId_EarnedAfterOne()
    {
        var def = AchievementRegistry.Find("first-disc-id")!;
        await Assert.That(def.Evaluate(Stats(discIds: 0)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(discIds: 1)).Earned).IsTrue();
    }

    [Test]
    public async Task InTheWorks_IsActivityOnlyAndAwardsNoPoints()
    {
        var def = AchievementRegistry.Find("in-the-works")!;
        await Assert.That(def.IsActivityOnly).IsTrue();
        await Assert.That(def.Points).IsEqualTo(0);
        await Assert.That(def.Evaluate(Stats(pending: 0)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(pending: 2)).Earned).IsTrue();
    }

    [Test]
    public async Task FormatCollector_EarnedAtDistinctFormatThreshold()
    {
        var def = AchievementRegistry.Find("format-collector-bronze")!;
        await Assert.That(def.Evaluate(Stats(formats: T.FormatCollectorBronze - 1)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(formats: T.FormatCollectorBronze)).Earned).IsTrue();
    }

    [Test]
    public async Task DecadeSpanner_EarnedAtDistinctDecadeThreshold()
    {
        var def = AchievementRegistry.Find("decade-spanner-bronze")!;
        await Assert.That(def.Evaluate(Stats(decades: T.DecadeSpannerBronze)).Earned).IsTrue();
    }

    [Test]
    public async Task BoxsetBuilder_EarnedAtBoxsetThreshold()
    {
        var def = AchievementRegistry.Find("boxset-builder-bronze")!;
        await Assert.That(def.Evaluate(Stats(boxsets: T.BoxsetBuilderBronze)).Earned).IsTrue();
    }

    [Test]
    public async Task GenreHopper_EarnedAtDistinctGenreThreshold()
    {
        var def = AchievementRegistry.Find("genre-hopper-bronze")!;
        await Assert.That(def.Evaluate(Stats(genres: T.GenreHopperBronze - 1)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(genres: T.GenreHopperBronze)).Earned).IsTrue();
    }

    [Test]
    public async Task GenreSpecialist_EarnedWhenManyReleasesInOneGenre()
    {
        var def = AchievementRegistry.Find("genre-specialist")!;
        await Assert.That(def.Evaluate(Stats(maxInGenre: T.GenreSpecialist)).Earned).IsTrue();
    }

    [Test]
    public async Task SeriesTiers_EarnedAtSeriesThresholds()
    {
        await Assert.That(AchievementRegistry.Find("series-contributor-silver")!.Evaluate(Stats(series: T.SeriesContributorSilver)).Earned).IsTrue();
        await Assert.That(AchievementRegistry.Find("series-contributor-gold")!.Evaluate(Stats(series: T.SeriesContributorGold - 1)).Earned).IsFalse();
    }

    [Test]
    public async Task ActiveStreak_EarnedAtConsecutiveMonthThreshold()
    {
        var def = AchievementRegistry.Find("active-streak-bronze")!;
        await Assert.That(def.Evaluate(Stats(streakMonths: T.ActiveStreakBronze - 1)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(streakMonths: T.ActiveStreakBronze)).Earned).IsTrue();
    }

    [Test]
    public async Task Comeback_IsOneTimeFlag()
    {
        var def = AchievementRegistry.Find("comeback")!;
        await Assert.That(def.Evaluate(Stats(comeback: false)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(comeback: true)).Earned).IsTrue();
    }

    [Test]
    public async Task FirstTry_IsOneTimeFlag()
    {
        var def = AchievementRegistry.Find("first-try")!;
        await Assert.That(def.Evaluate(Stats(firstTry: false)).Earned).IsFalse();
        await Assert.That(def.Evaluate(Stats(firstTry: true)).Earned).IsTrue();
    }

    [Test]
    public async Task EveryEarnableIconHasAnAssetFile()
    {
        var badgesDir = System.IO.Path.Combine(
            FindRepoRoot(), "code", "TheDiscDb", "wwwroot", "badges");

        foreach (var def in AchievementRegistry.All)
        {
            var path = System.IO.Path.Combine(badgesDir, def.Icon + ".svg");
            await Assert.That(System.IO.File.Exists(path)).IsTrue();
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "code", "TheDiscDb", "wwwroot", "badges")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
