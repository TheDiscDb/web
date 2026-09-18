using TheDiscDb.Components.Pages;

namespace TheDiscDb.UnitTests.Components.Pages;

public class ContributionStatsTests
{
    [Test]
    public async Task BuildCumulativeSeries_GroupsByUtcDateAndFillsMissingDays()
    {
        var dates = new[]
        {
            new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.FromHours(-2)),
            new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 4, 8, 0, 0, TimeSpan.Zero)
        };

        var result = ContributionStats.BuildCumulativeSeries(dates);

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0]).IsEqualTo(
            new ContributionStats.ContributionStatsPoint(new DateTime(2026, 1, 2), 2));
        await Assert.That(result[1]).IsEqualTo(
            new ContributionStats.ContributionStatsPoint(new DateTime(2026, 1, 3), 2));
        await Assert.That(result[2]).IsEqualTo(
            new ContributionStats.ContributionStatsPoint(new DateTime(2026, 1, 4), 3));
    }

    [Test]
    public async Task BuildCumulativeSeries_NoContributions_ReturnsEmptySeries()
    {
        var result = ContributionStats.BuildCumulativeSeries([]);

        await Assert.That(result).IsEmpty();
    }
}
