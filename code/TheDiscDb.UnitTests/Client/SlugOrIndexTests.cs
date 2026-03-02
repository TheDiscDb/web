namespace TheDiscDb.UnitTests.Client;

public class SlugOrIndexTests
{
    [Test]
    public async Task Create_WithNumericString_SetsIndex()
    {
        var soi = SlugOrIndex.Create("5");

        await Assert.That(soi.Index).IsEqualTo(5);
        await Assert.That(soi.Slug).IsNull();
    }

    [Test]
    public async Task Create_WithNonNumericString_SetsSlug()
    {
        var soi = SlugOrIndex.Create("my-slug");

        await Assert.That(soi.Slug).IsEqualTo("my-slug");
        await Assert.That(soi.Index).IsNull();
    }

    [Test]
    public async Task Create_WithNullOrEmpty_DefaultsToIndexZero()
    {
        var soiNull = SlugOrIndex.Create(null);
        var soiEmpty = SlugOrIndex.Create("");
        var soiWhitespace = SlugOrIndex.Create("   ");

        await Assert.That(soiNull.Index).IsEqualTo(0);
        await Assert.That(soiEmpty.Index).IsEqualTo(0);
        await Assert.That(soiWhitespace.Index).IsEqualTo(0);
    }

    [Test]
    public async Task Create_WithSlugAndIndex_SetsBoth()
    {
        var soi = SlugOrIndex.Create("my-slug", 3);

        await Assert.That(soi.Slug).IsEqualTo("my-slug");
        await Assert.That(soi.Index).IsEqualTo(3);
    }

    [Test]
    public async Task Create_WithBothNull_ThrowsArgumentNullException()
    {
        ArgumentNullException? caught = null;
        try
        {
            SlugOrIndex.Create(null, null);
        }
        catch (ArgumentNullException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    [Test]
    public async Task UrlValue_PrefersSlug_OverIndex()
    {
        var soi = SlugOrIndex.Create("my-slug", 3);

        await Assert.That(soi.UrlValue).IsEqualTo("my-slug");
    }

    [Test]
    public async Task UrlValue_FallsBackToIndex_WhenNoSlug()
    {
        var soi = SlugOrIndex.Create("5");

        await Assert.That(soi.UrlValue).IsEqualTo("5");
    }

    [Test]
    public async Task UrlValue_ReturnsDefaultValue_WhenNeitherSet()
    {
        var soi = new SlugOrIndex();

        await Assert.That(soi.UrlValue).IsEqualTo(SlugOrIndex.DefaultValue);
    }

    [Test]
    public async Task ImplicitConversion_FromNumericString_SetsIndex()
    {
        SlugOrIndex soi = "42";

        await Assert.That(soi.Index).IsEqualTo(42);
    }

    [Test]
    public async Task ImplicitConversion_FromNonNumericString_SetsSlug()
    {
        SlugOrIndex soi = "some-slug";

        await Assert.That(soi.Slug).IsEqualTo("some-slug");
    }

    [Test]
    public async Task Equals_SameSlugs_CaseInsensitive_ReturnsTrue()
    {
        var a = SlugOrIndex.Create("My-Slug");
        var b = SlugOrIndex.Create("my-slug");

        await Assert.That(a.Equals(b)).IsTrue();
    }

    [Test]
    public async Task Equals_SameIndex_ReturnsTrue()
    {
        var a = SlugOrIndex.Create("5");
        var b = SlugOrIndex.Create("5");

        await Assert.That(a.Equals(b)).IsTrue();
    }

    [Test]
    public async Task Equals_DifferentSlugs_ReturnsFalse()
    {
        var a = SlugOrIndex.Create("slug-a");
        var b = SlugOrIndex.Create("slug-b");

        await Assert.That(a.Equals(b)).IsFalse();
    }

    [Test]
    public async Task Equals_DifferentIndex_ReturnsFalse()
    {
        var a = SlugOrIndex.Create("1");
        var b = SlugOrIndex.Create("2");

        await Assert.That(a.Equals(b)).IsFalse();
    }

    [Test]
    public async Task ToString_WithIndex_ReturnsIndexString()
    {
        var soi = SlugOrIndex.Create("7");

        await Assert.That(soi.ToString()).IsEqualTo("7");
    }

    [Test]
    public async Task ToString_WithSlug_ReturnsSlug()
    {
        var soi = SlugOrIndex.Create("my-slug");

        await Assert.That(soi.ToString()).IsEqualTo("my-slug");
    }

    [Test]
    public async Task EqualityOperator_EqualValues_ReturnsTrue()
    {
        SlugOrIndex? a = SlugOrIndex.Create("test");
        SlugOrIndex? b = SlugOrIndex.Create("test");

        await Assert.That(a == b).IsTrue();
        await Assert.That(a != b).IsFalse();
    }

    [Test]
    public async Task GetHashCode_SameSlug_ReturnsSameHash()
    {
        var a = SlugOrIndex.Create("slug");
        var b = SlugOrIndex.Create("slug");

        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
    }
}
