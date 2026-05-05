using TheDiscDb.Naming;

namespace TheDiscDb.UnitTests.Naming;

public class FileNameTemplateResolverTests
{
    [Test]
    public async Task Resolve_NoOverride_ReturnsDefaultTemplate()
    {
        var resolver = new FileNameTemplateResolver();

        var template = resolver.Resolve(ItemTypeNames.MainMovie);

        await Assert.That(template).IsNotNull();
    }

    [Test]
    public async Task Resolve_WithOverride_PreferOverride()
    {
        var overrides = new Dictionary<string, string>
        {
            [ItemTypeNames.MainMovie] = "{title}",
        };
        var resolver = new FileNameTemplateResolver(overrides);

        var ctx = new NamingContext { Title = "Hello", FullTitle = "Hello (2020)", Edition = "X", Resolution = "Y" };
        var actual = resolver.Format(ItemTypeNames.MainMovie, ctx);

        await Assert.That(actual).IsEqualTo("Hello");
    }

    [Test]
    public async Task Resolve_InvalidOverride_FallsBackToDefault()
    {
        var overrides = new Dictionary<string, string>
        {
            [ItemTypeNames.MainMovie] = "{notatoken}",
        };
        var resolver = new FileNameTemplateResolver(overrides);

        var ctx = new NamingContext { FullTitle = "Hello", Edition = "X", Resolution = "Y" };
        var actual = resolver.Format(ItemTypeNames.MainMovie, ctx);

        await Assert.That(actual).IsEqualTo("Hello [Y].mkv");
    }

    [Test]
    public async Task Resolve_UnknownItemType_ReturnsNull()
    {
        var resolver = new FileNameTemplateResolver();

        var template = resolver.Resolve("Bogus");

        await Assert.That(template).IsNull();
    }

    [Test]
    public async Task Format_UnknownItemType_ReturnsEmptyString()
    {
        var resolver = new FileNameTemplateResolver();

        var actual = resolver.Format("Bogus", new NamingContext { Title = "x" });

        await Assert.That(actual).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Resolve_CachesParsedTemplates()
    {
        var resolver = new FileNameTemplateResolver();

        var first = resolver.Resolve(ItemTypeNames.MainMovie);
        var second = resolver.Resolve(ItemTypeNames.MainMovie);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task Resolve_IsCaseInsensitiveForItemType()
    {
        var resolver = new FileNameTemplateResolver();

        var lower = resolver.Resolve("mainmovie");
        var upper = resolver.Resolve("MainMovie");

        await Assert.That(lower).IsNotNull();
        await Assert.That(upper).IsNotNull();
    }
}
