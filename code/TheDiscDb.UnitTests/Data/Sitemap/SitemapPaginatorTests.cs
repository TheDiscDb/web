namespace TheDiscDb.UnitTests.Data.Sitemap;

using TheDiscDb.Web.Sitemap;

public class SitemapPaginatorTests
{
    private static SitemapNode MakeNode(int index) => new()
    {
        Url = $"https://example.com/item-{index}",
    };

    [Test]
    public async Task Paginate_ZeroNodes_ReturnsSingleEmptyPageWithCategoryStem()
    {
        var pages = SitemapPaginator.Paginate("movies", Array.Empty<SitemapNode>(), pageSize: 10);

        await Assert.That(pages.Count).IsEqualTo(1);
        await Assert.That(pages[0].Stem).IsEqualTo("movies");
        await Assert.That(pages[0].Nodes).IsEmpty();
    }

    [Test]
    public async Task Paginate_ExactMultipleOfPageSize_ReturnsExactPageCountWithNoRemainderPage()
    {
        var nodes = Enumerable.Range(0, 6).Select(MakeNode).ToList();

        var pages = SitemapPaginator.Paginate("movies-discs", nodes, pageSize: 3);

        await Assert.That(pages.Count).IsEqualTo(2);
        await Assert.That(pages[0].Stem).IsEqualTo("movies-discs");
        await Assert.That(pages[0].Nodes.Count).IsEqualTo(3);
        await Assert.That(pages[1].Stem).IsEqualTo("movies-discs-2");
        await Assert.That(pages[1].Nodes.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Paginate_RemainderNodes_ProducesSmallerFinalPage()
    {
        var nodes = Enumerable.Range(0, 5).Select(MakeNode).ToList();

        var pages = SitemapPaginator.Paginate("series-titles", nodes, pageSize: 2);

        await Assert.That(pages.Count).IsEqualTo(3);
        await Assert.That(pages[0].Stem).IsEqualTo("series-titles");
        await Assert.That(pages[0].Nodes.Count).IsEqualTo(2);
        await Assert.That(pages[1].Stem).IsEqualTo("series-titles-2");
        await Assert.That(pages[1].Nodes.Count).IsEqualTo(2);
        await Assert.That(pages[2].Stem).IsEqualTo("series-titles-3");
        await Assert.That(pages[2].Nodes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Paginate_SingleNodeUnderPageSize_ReturnsOnePageWithNoSuffix()
    {
        var nodes = new[] { MakeNode(0) };

        var pages = SitemapPaginator.Paginate("boxsets", nodes, pageSize: 50_000);

        await Assert.That(pages.Count).IsEqualTo(1);
        await Assert.That(pages[0].Stem).IsEqualTo("boxsets");
        await Assert.That(pages[0].Nodes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Paginate_InvalidPageSize_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Task.FromResult(SitemapPaginator.Paginate("movies", Array.Empty<SitemapNode>(), pageSize: 0)));
        await Assert.That(ex!.ParamName).IsEqualTo("pageSize");
    }
}
