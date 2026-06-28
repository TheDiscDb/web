namespace TheDiscDb.UnitTests.Data.Changes;

using System.Text.Json;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;

public class ChangeFactoryTests
{
    private static ChangeFactory CreateFactoryWithReleaseFieldsBuilder()
    {
        var builders = new IChangeBuilder[]
        {
            new ChangeBuilder<ReleaseFieldsDetails>(
                ReleaseFieldsUpdate.Key,
                d => new ReleaseFieldsUpdate(d)),
        };
        return new ChangeFactory(builders);
    }

    [Test]
    public async Task Create_ReturnsRegisteredChange_ForKnownTypeKey()
    {
        var factory = CreateFactoryWithReleaseFieldsBuilder();
        var json = JsonSerializer.Serialize(new ReleaseFieldsDetails(
            MediaItemSlug: "the-movie",
            BoxsetSlug: null,
            ReleaseSlug: "the-release-slug",
            Title: "Test",
            RegionCode: "US",
            Locale: "en-US",
            Year: 2020,
            Upc: null,
            Isbn: null,
            Asin: null,
            ReleaseDate: DateTimeOffset.UnixEpoch),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var change = factory.Create(ReleaseFieldsUpdate.Key, json);

        await Assert.That(change).IsTypeOf<ReleaseFieldsUpdate>();
        await Assert.That(change.TypeKey).IsEqualTo(ReleaseFieldsUpdate.Key);
    }

    [Test]
    public async Task Create_Throws_ForUnknownTypeKey()
    {
        var factory = CreateFactoryWithReleaseFieldsBuilder();
        var ex = await Assert.ThrowsAsync<UnknownChangeTypeException>(
            () => Task.FromResult(factory.Create("not.a.real.key", "{}")));
        await Assert.That(ex!.TypeKey).IsEqualTo("not.a.real.key");
    }

    [Test]
    public async Task Create_Throws_ForEmptyJson()
    {
        var factory = CreateFactoryWithReleaseFieldsBuilder();
        var ex = await Assert.ThrowsAsync<InvalidChangeJsonException>(
            () => Task.FromResult(factory.Create(ReleaseFieldsUpdate.Key, "")));
        await Assert.That(ex!.TypeKey).IsEqualTo(ReleaseFieldsUpdate.Key);
    }

    [Test]
    public async Task Create_Throws_ForMalformedJson()
    {
        var factory = CreateFactoryWithReleaseFieldsBuilder();
        var ex = await Assert.ThrowsAsync<InvalidChangeJsonException>(
            () => Task.FromResult(factory.Create(ReleaseFieldsUpdate.Key, "{ not valid json")));
        await Assert.That(ex!.TypeKey).IsEqualTo(ReleaseFieldsUpdate.Key);
    }

    [Test]
    public async Task Constructor_Throws_OnDuplicateTypeKeys()
    {
        var builders = new IChangeBuilder[]
        {
            new ChangeBuilder<ReleaseFieldsDetails>(ReleaseFieldsUpdate.Key, d => new ReleaseFieldsUpdate(d)),
            new ChangeBuilder<ReleaseFieldsDetails>(ReleaseFieldsUpdate.Key, d => new ReleaseFieldsUpdate(d)),
        };

        var ex = await Assert.ThrowsAsync<DuplicateChangeBuilderException>(
            () => Task.FromResult(new ChangeFactory(builders)));
        await Assert.That(ex!.TypeKey).IsEqualTo(ReleaseFieldsUpdate.Key);
    }

    [Test]
    public async Task RegisteredTypeKeys_ExposesAllRegistered()
    {
        var factory = CreateFactoryWithReleaseFieldsBuilder();
        await Assert.That(factory.RegisteredTypeKeys).Contains(ReleaseFieldsUpdate.Key);
    }
}
