using TheDiscDb.Contributions.OpticalDiscManifest;
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
            CreateItem(1, DiscLogManifestComparisonStatus.Match, "blu-ray", "1.0", "1.17.8", "Mozilla/5.0 Chrome/120.0"),
            CreateItem(2, DiscLogManifestComparisonStatus.Mismatch, "dvd", "1.0", "1.17.7", "Mozilla/5.0 Firefox/119.0"),
            CreateItem(3, DiscLogManifestComparisonStatus.Match, "blu-ray", "1.1", "1.17.8", "TheDiscDb.DiscScan.Cli/1.0"),
        };

        var dashboard = DiscScanParityAggregator.CreateDashboard(latest);

        await Assert.That(dashboard.Overall.Total).IsEqualTo(3);
        await Assert.That(dashboard.Overall.Matches).IsEqualTo(2);
        await Assert.That(dashboard.NonMatches.Single().DiscId).IsEqualTo(2);
        await Assert.That(dashboard.ByFormat.Single(item => item.Label == "blu-ray").Stats.Matches).IsEqualTo(2);
        await Assert.That(dashboard.ByBrowser.Single(item => item.Label == "Firefox").Stats.Total).IsEqualTo(1);
    }

    [Test]
    public async Task CreateDashboard_SeparatesIdentifyMetricCoverageAndTechnicalParity()
    {
        var latest = new[]
        {
            CreateItem(1, DiscLogManifestComparisonStatus.Mismatch, "blu-ray", "1", "1", "CLI") with
            { IdentifyExperience = new(1, IdentifyExperienceStatus.Match, 1, 1, 1, []) },
            CreateItem(2, DiscLogManifestComparisonStatus.Match, "blu-ray", "1", "1", "CLI") with
            { IdentifyExperience = new(1, IdentifyExperienceStatus.Mismatch, 1, 1, 0, []) },
            CreateItem(3, DiscLogManifestComparisonStatus.Error, "dvd", "1", "1", "CLI") with
            { IdentifyExperience = IdentifyExperienceComparison.NotComparable("error") },
            CreateItem(4, DiscLogManifestComparisonStatus.Match, "dvd", "1", "1", "CLI"),
            CreateItem(5, DiscLogManifestComparisonStatus.Match, "dvd", "1", "1", "CLI") with
            { IdentifyExperience = new(99, IdentifyExperienceStatus.Match, 1, 1, 1, []) }
        };
        var dashboard = DiscScanParityAggregator.CreateDashboard(latest);
        await Assert.That(dashboard.IdentifyExperience.Measured).IsEqualTo(2);
        await Assert.That(dashboard.IdentifyExperience.Matches).IsEqualTo(1);
        await Assert.That(dashboard.IdentifyExperience.Mismatches).IsEqualTo(1);
        await Assert.That(dashboard.IdentifyExperience.NotComparable).IsEqualTo(1);
        await Assert.That(dashboard.IdentifyExperience.NotMeasured).IsEqualTo(2);
        await Assert.That(dashboard.IdentifyExperienceNonMatches.Count).IsEqualTo(4);
        await Assert.That(dashboard.ByFormat.Single(g => g.Label == "blu-ray").IdentifyExperience.Matches).IsEqualTo(1);
        await Assert.That(latest[3].IdentifyExperienceLabel).IsEqualTo("Not measured");
    }

    private static ContributionDiscComparisonListItem CreateItem(
        int discId,
        DiscLogManifestComparisonStatus status,
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
            status == DiscLogManifestComparisonStatus.Match ? 10 : 9,
            true,
            status,
            "{}");
}
