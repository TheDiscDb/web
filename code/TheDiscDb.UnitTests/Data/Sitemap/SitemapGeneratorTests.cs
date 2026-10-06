namespace TheDiscDb.UnitTests.Data.Sitemap;

using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Sitemap;

/// <summary>
/// Verifies that SitemapGenerator.BuildByCategory routes URLs into the expected
/// per-category buckets (movie vs. series, and disc pages vs. title/episode pages),
/// since the whole point of the category split is to keep each child sitemap file
/// well under Google's 50,000-URL limit.
/// </summary>
public class SitemapGeneratorTests
{
    private const string SiteBase = "https://example.com";

    private class TestDbContextFactory(string dbName) : IDbContextFactory<SqlServerDataContext>
    {
        public SqlServerDataContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<SqlServerDataContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new SqlServerDataContext(options);
        }
    }

    private static SitemapGenerator CreateGenerator(out SqlServerDataContext seedDb)
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);
        seedDb = factory.CreateDbContext();
        return new SitemapGenerator(factory);
    }

    private static MediaItem SeedMediaItem(SqlServerDataContext db, string type, string slug)
    {
        var itemRef = new DiscItemReference
        {
            Title = "Feature",
            Type = "movie",
        };

        var title = new Title
        {
            Index = 0,
            SourceFile = "00001.mpls",
            Item = itemRef,
        };

        var disc = new Disc
        {
            Slug = "disc-one",
            Index = 0,
            Name = "Disc One",
            Format = "Blu-ray",
        };
        disc.Titles.Add(title);

        var releaseDisc = new ReleaseDisc
        {
            Slug = "disc-one",
            Index = 0,
            Name = "Disc One",
            Disc = disc,
        };

        var mediaItem = new MediaItem
        {
            Slug = slug,
            Title = slug,
            Type = type,
            Year = 2020,
        };

        var release = new Release
        {
            Slug = "release-one",
            Title = "Release One",
            RegionCode = "US",
            Locale = "en-US",
            Year = 2020,
            MediaItem = mediaItem,
        };
        release.Discs.Add(releaseDisc);
        mediaItem.Releases.Add(release);

        db.Add(mediaItem);
        db.SaveChanges();

        return mediaItem;
    }

    [Test]
    public async Task BuildByCategory_MovieItem_RoutesToMovieBucketsOnly()
    {
        var generator = CreateGenerator(out var db);
        SeedMediaItem(db, "Movie", "the-movie");

        var categories = await generator.BuildByCategory(SiteBase);

        await Assert.That(categories[SitemapCategories.Movies].Count).IsEqualTo(1);
        await Assert.That(categories[SitemapCategories.MoviesReleases].Count).IsEqualTo(1);
        await Assert.That(categories[SitemapCategories.MoviesDiscs].Count).IsEqualTo(1);
        await Assert.That(categories[SitemapCategories.MoviesTitles].Count).IsEqualTo(1);

        await Assert.That(categories[SitemapCategories.Series].Count).IsEqualTo(0);
        await Assert.That(categories[SitemapCategories.SeriesReleases].Count).IsEqualTo(0);
        await Assert.That(categories[SitemapCategories.SeriesDiscs].Count).IsEqualTo(0);
        await Assert.That(categories[SitemapCategories.SeriesTitles].Count).IsEqualTo(0);

        await Assert.That(categories[SitemapCategories.Movies].Single().Url).Contains("/movie/the-movie");
    }

    [Test]
    public async Task BuildByCategory_SeriesItem_RoutesToSeriesBucketsOnly()
    {
        var generator = CreateGenerator(out var db);
        SeedMediaItem(db, "Series", "the-series");

        var categories = await generator.BuildByCategory(SiteBase);

        await Assert.That(categories[SitemapCategories.Series].Count).IsEqualTo(1);
        await Assert.That(categories[SitemapCategories.SeriesReleases].Count).IsEqualTo(1);
        await Assert.That(categories[SitemapCategories.SeriesDiscs].Count).IsEqualTo(1);
        await Assert.That(categories[SitemapCategories.SeriesTitles].Count).IsEqualTo(1);

        await Assert.That(categories[SitemapCategories.Movies].Count).IsEqualTo(0);
        await Assert.That(categories[SitemapCategories.MoviesReleases].Count).IsEqualTo(0);
        await Assert.That(categories[SitemapCategories.MoviesDiscs].Count).IsEqualTo(0);
        await Assert.That(categories[SitemapCategories.MoviesTitles].Count).IsEqualTo(0);

        await Assert.That(categories[SitemapCategories.Series].Single().Url).Contains("/series/the-series");
    }

    [Test]
    public async Task BuildByCategory_DiscAndTitleUrls_AreInSeparateBuckets()
    {
        var generator = CreateGenerator(out var db);
        SeedMediaItem(db, "Movie", "the-movie");

        var categories = await generator.BuildByCategory(SiteBase);

        var discUrl = categories[SitemapCategories.MoviesDiscs].Single().Url;
        var titleUrl = categories[SitemapCategories.MoviesTitles].Single().Url;

        await Assert.That(discUrl).Contains("/discs/disc-one");
        await Assert.That(titleUrl).Contains("/discs/disc-one/");
        await Assert.That(titleUrl.Length > discUrl.Length).IsTrue();
    }

    [Test]
    public async Task BuildByCategory_AllCategories_ArePresentEvenWhenEmpty()
    {
        var generator = CreateGenerator(out _);

        var categories = await generator.BuildByCategory(SiteBase);

        foreach (var category in SitemapCategories.All)
        {
            await Assert.That(categories.ContainsKey(category)).IsTrue();

            // "leaderboard" always includes the static /leaderboard page itself, even with
            // no contributors, so it's never empty.
            if (category == SitemapCategories.Leaderboard)
            {
                await Assert.That(categories[category].Count).IsEqualTo(1);
            }
            else
            {
                await Assert.That(categories[category]).IsEmpty();
            }
        }
    }
}
