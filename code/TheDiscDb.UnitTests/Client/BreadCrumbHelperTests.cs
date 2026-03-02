using TheDiscDb.InputModels;

namespace TheDiscDb.UnitTests.Client;

public class BreadCrumbHelperTests
{
    [Test]
    public async Task TruncateForDescription_NullOrEmpty_ReturnsEmpty()
    {
        await Assert.That(BreadCrumbHelper.TruncateForDescription(null, 100)).IsEqualTo(string.Empty);
        await Assert.That(BreadCrumbHelper.TruncateForDescription("", 100)).IsEqualTo(string.Empty);
        await Assert.That(BreadCrumbHelper.TruncateForDescription("   ", 100)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TruncateForDescription_ShortText_ReturnsUnchanged()
    {
        var result = BreadCrumbHelper.TruncateForDescription("Hello World", 100);

        await Assert.That(result).IsEqualTo("Hello World");
    }

    [Test]
    public async Task TruncateForDescription_LongText_TruncatesAtWordBoundary()
    {
        var result = BreadCrumbHelper.TruncateForDescription("The quick brown fox jumps over the lazy dog", 20);

        await Assert.That(result).Contains("…");
        await Assert.That(result.Length).IsLessThanOrEqualTo(25); // maxLength + ellipsis allowance
    }

    [Test]
    public async Task TruncateForDescription_ExactLength_ReturnsUnchanged()
    {
        var text = "Exactly ten";
        var result = BreadCrumbHelper.TruncateForDescription(text, text.Length);

        await Assert.That(result).IsEqualTo(text);
    }

    [Test]
    public async Task GetRootContributionLink_ReturnsExpectedValues()
    {
        var link = BreadCrumbHelper.GetRootContributionLink();

        await Assert.That(link.Text).IsEqualTo("Contribute");
        await Assert.That(link.Url).IsEqualTo("/contribute");
    }

    [Test]
    public async Task GetRootLink_Movie_ReturnsMoviesLink()
    {
        var item = new TestDisplayItem { Type = "movie" };
        var link = BreadCrumbHelper.GetRootLink(item);

        await Assert.That(link.Text).IsEqualTo("Movies");
        await Assert.That(link.Url).IsEqualTo("/movies");
    }

    [Test]
    public async Task GetRootLink_Series_ReturnsSeriesLink()
    {
        var item = new TestDisplayItem { Type = "series" };
        var link = BreadCrumbHelper.GetRootLink(item);

        await Assert.That(link.Text).IsEqualTo("Series");
        await Assert.That(link.Url).IsEqualTo("/series");
    }

    [Test]
    public async Task GetRootLink_Boxset_ReturnsBoxsetsLink()
    {
        var item = new TestDisplayItem { Type = "boxset" };
        var link = BreadCrumbHelper.GetRootLink(item);

        await Assert.That(link.Text).IsEqualTo("Boxsets");
        await Assert.That(link.Url).IsEqualTo("/boxsets");
    }

    [Test]
    public async Task GetRootLink_NullType_ReturnsEmpty()
    {
        var item = new TestDisplayItem { Type = null };
        var link = BreadCrumbHelper.GetRootLink(item);

        await Assert.That(link.Text).IsEqualTo(string.Empty);
        await Assert.That(link.Url).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetMediaItemLink_ValidItem_ReturnsCorrectUrl()
    {
        var item = new TestDisplayItem { Title = "The Matrix", Slug = "the-matrix", Type = "movie" };
        var link = BreadCrumbHelper.GetMediaItemLink(item);

        await Assert.That(link.Text).IsEqualTo("The Matrix");
        await Assert.That(link.Url).IsEqualTo("/movie/the-matrix");
    }

    [Test]
    public async Task GetMediaItemLink_NullTitle_ReturnsEmpty()
    {
        var item = new TestDisplayItem { Title = null, Slug = "slug", Type = "movie" };
        var link = BreadCrumbHelper.GetMediaItemLink(item);

        await Assert.That(link.Text).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task BuildCanonicalLink_ValidLink_PrependsBaseUrl()
    {
        var result = BreadCrumbHelper.BuildCanonicalLink(("Test", "/Movie/Test"));

        await Assert.That(result).IsEqualTo("https://thediscdb.com/movie/test");
    }

    [Test]
    public async Task BuildCanonicalLink_EmptyUrl_ReturnsEmpty()
    {
        var result = BreadCrumbHelper.BuildCanonicalLink(("", ""));

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetFullName_WithName_ReturnsNameAndFormat()
    {
        var disc = new TestDisc { Name = "Special Features", Format = "Blu-ray", Index = 1 };
        var result = disc.GetFullName();

        await Assert.That(result).IsEqualTo("Special Features (Blu-ray)");
    }

    [Test]
    public async Task GetFullName_WithoutName_ReturnsDiscIndexAndFormat()
    {
        var disc = new TestDisc { Name = null, Format = "4K UHD", Index = 2 };
        var result = disc.GetFullName();

        await Assert.That(result).IsEqualTo("Disc 2 (4K UHD)");
    }

    private class TestDisplayItem : IDisplayItem
    {
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? ImageUrl { get; set; }
        public string? Type { get; set; }
    }

    private class TestDisc : IDisc
    {
        public int Index { get; set; }
        public string? Slug { get; set; }
        public string? Name { get; set; }
        public string? Format { get; set; }
    }
}
