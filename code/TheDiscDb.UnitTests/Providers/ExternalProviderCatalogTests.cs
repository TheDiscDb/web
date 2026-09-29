namespace TheDiscDb.UnitTests.Providers;

using TheDiscDb.InputModels;

public class ExternalProviderCatalogTests
{
    [Test]
    public async Task BuildUrl_SubstitutesId()
    {
        var provider = ExternalProviderCatalog.Find("bluray");

        await Assert.That(provider).IsNotNull();
        await Assert.That(provider!.BuildUrl("263678")).IsEqualTo("https://www.blu-ray.com/movies/movies.php?id=263678");
    }

    [Test]
    public async Task BuildUrl_ReturnsNull_WhenIdIsNullOrWhitespace()
    {
        var provider = ExternalProviderCatalog.Find("dvdcompare")!;

        await Assert.That(provider.BuildUrl(null)).IsNull();
        await Assert.That(provider.BuildUrl("")).IsNull();
        await Assert.That(provider.BuildUrl("   ")).IsNull();
    }

    [Test]
    public async Task BuildUrl_TrimsId()
    {
        var provider = ExternalProviderCatalog.Find("dvdtalk")!;

        await Assert.That(provider.BuildUrl(" 45391 ")).IsEqualTo("https://www.dvdtalk.com/reviews/read/45391");
    }

    [Test]
    public async Task Find_IsCaseInsensitive()
    {
        await Assert.That(ExternalProviderCatalog.Find("BLURAY")).IsNotNull();
    }

    [Test]
    public async Task Find_ReturnsNull_ForUnknownProvider()
    {
        await Assert.That(ExternalProviderCatalog.Find("not-a-real-provider")).IsNull();
    }

    [Test]
    public async Task GetStoredId_And_SetStoredId_RoundTripEachProvider()
    {
        var externalIds = new ExternalIds();

        foreach (var provider in ExternalProviderCatalog.Providers)
        {
            ExternalProviderCatalog.SetStoredId(externalIds, provider.Id, $"id-{provider.Id}");
        }

        foreach (var provider in ExternalProviderCatalog.Providers)
        {
            await Assert.That(ExternalProviderCatalog.GetStoredId(externalIds, provider.Id)).IsEqualTo($"id-{provider.Id}");
        }
    }
}
