namespace TheDiscDb.UnitTests.GraphQL.Contribute;

using TheDiscDb.GraphQL.Contribute.Mutations;
using TheDiscDb.Services.Contributions;
using TheDiscDb.Web.Data;

public class DiscLogSourceSelectorTests
{
    [Test]
    public async Task Select_DefaultModeWithBothSourcesAndNoItems_PrefersManifest()
    {
        var disc = new UserContributionDisc();

        var preference = DiscLogSourceSelector.Select(disc, DiscScanMode.Default, hasLogs: true, hasManifest: true);

        await Assert.That(preference).IsEqualTo(DiscLogSourcePreference.ManifestFirst);
    }

    [Test]
    public async Task Select_DefaultModeWithSavedItems_KeepsLogFirst()
    {
        var disc = new UserContributionDisc();
        disc.Items.Add(new UserContributionDiscItem());

        var preference = DiscLogSourceSelector.Select(disc, DiscScanMode.Default, hasLogs: true, hasManifest: true);

        await Assert.That(preference).IsEqualTo(DiscLogSourcePreference.LogFirst);
    }

    [Test]
    public async Task Select_OptionalModeWithBothSources_PrefersLog()
    {
        var disc = new UserContributionDisc();

        var preference = DiscLogSourceSelector.Select(disc, DiscScanMode.Optional, hasLogs: true, hasManifest: true);

        await Assert.That(preference).IsEqualTo(DiscLogSourcePreference.LogFirst);
    }
}
