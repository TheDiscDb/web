namespace TheDiscDb.UnitTests.Services.Admin;

using TheDiscDb.Services.Admin;
using TheDiscDb.Web.Data;

public class DiscScanParityAggregatorTests
{
    [Test]
    [Arguments("Mozilla/5.0 Edg/120.0", "Edge")]
    [Arguments("Mozilla/5.0 Chrome/120.0 Safari/537.36", "Chrome")]
    [Arguments("Mozilla/5.0 Firefox/119.0", "Firefox")]
    [Arguments("TheDiscDb.DiscScan.Cli/1.0", "CLI")]
    [Arguments(null, "Other")]
    [Arguments("curl/8.0", "Other")]
    public async Task BrowserName_DerivesShortName(string? userAgent, string expected)
    {
        string actual = DiscScanParityAggregator.BrowserName(userAgent);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task CreateDashboard_UsesLatestComparisonsAndAggregatesMatches()
    {
        var latest = new[]
        {
            CreateItem(1, UserContributionDiscComparisonStatus.Match, "blu-ray", "1.0", "1.17.8", "Mozilla/5.0 Chrome/120.0"),
            CreateItem(2, UserContributionDiscComparisonStatus.Mismatch, "dvd", "1.0", "1.17.7", "Mozilla/5.0 Firefox/119.0"),
            CreateItem(3, UserContributionDiscComparisonStatus.Match, "blu-ray", "1.1", "1.17.8", "TheDiscDb.DiscScan.Cli/1.0"),
        };

        var dashboard = DiscScanParityAggregator.CreateDashboard(latest);

        await Assert.That(dashboard.Overall.Total).IsEqualTo(3);
        await Assert.That(dashboard.Overall.Matches).IsEqualTo(2);
        await Assert.That(dashboard.NonMatches.Single().DiscId).IsEqualTo(2);
        await Assert.That(dashboard.ByFormat.Single(item => item.Label == "blu-ray").Stats.Matches).IsEqualTo(2);
        await Assert.That(dashboard.ByBrowser.Single(item => item.Label == "Firefox").Stats.Total).IsEqualTo(1);
    }

    private static ContributionDiscComparisonListItem CreateItem(
        int discId,
        UserContributionDiscComparisonStatus status,
        string format,
        string producerVersion,
        string makeMkvVersion,
        string userAgent)
        => new(
            discId,
            discId,
            100 + discId,
            $"Release {discId}",
            discId,
            $"Disc {discId}",
            DateTimeOffset.UtcNow,
            format,
            "thediscdb",
            producerVersion,
            makeMkvVersion,
            userAgent,
            10,
            10,
            status == UserContributionDiscComparisonStatus.Match ? 10 : 9,
            true,
            status,
            "{}");
}
