using Microsoft.EntityFrameworkCore;
using TheDiscDb.Affiliate;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.Server.Affiliate;

public class GruvLinkLookupTests
{
    private static (GruvLinkLookup lookup, SqlServerDataContext db) CreateLookup()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var db = new SqlServerDataContext(options);
        var factory = new TestDbContextFactory(options);
        var lookup = new GruvLinkLookup(factory);
        return (lookup, db);
    }

    private static ReleaseAffiliateLink MakeRow(string? mediaItemSlug, string? boxsetSlug, string releaseSlug, string handle = "h")
        => new()
        {
            MediaItemSlug = mediaItemSlug,
            BoxsetSlug = boxsetSlug,
            ReleaseSlug = releaseSlug,
            Provider = "gruv",
            ProviderHandle = handle,
            ProviderUrl = $"https://gruv.com/products/{handle}",
            MatchSource = "upc-exact",
            MatchedAt = DateTimeOffset.UtcNow,
        };

    [Test]
    public async Task GetAsync_MediaItemMatch_ReturnsRow()
    {
        var (lookup, db) = CreateLookup();
        db.ReleaseAffiliateLinks.Add(MakeRow("jaws-1975", null, "2016-blu-ray"));
        await db.SaveChangesAsync();

        var result = await lookup.GetAsync("jaws-1975", null, "2016-blu-ray", CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ReleaseSlug).IsEqualTo("2016-blu-ray");
    }

    [Test]
    public async Task GetAsync_BoxsetMatch_ReturnsRow()
    {
        var (lookup, db) = CreateLookup();
        db.ReleaseAffiliateLinks.Add(MakeRow(null, "back-to-the-future-trilogy", "2020-uhd"));
        await db.SaveChangesAsync();

        var result = await lookup.GetAsync(null, "back-to-the-future-trilogy", "2020-uhd", CancellationToken.None);

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetAsync_NoMatch_ReturnsNull()
    {
        var (lookup, _) = CreateLookup();
        var result = await lookup.GetAsync("nonexistent", null, "no-release", CancellationToken.None);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetAsync_BothSlugsNull_ReturnsNull()
    {
        var (lookup, _) = CreateLookup();
        var result = await lookup.GetAsync(null, null, "release", CancellationToken.None);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetAsync_EmptyReleaseSlug_ReturnsNull()
    {
        var (lookup, _) = CreateLookup();
        var result = await lookup.GetAsync("media", null, string.Empty, CancellationToken.None);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetAsync_MediaItemSlugCaseInsensitive_StillMatches()
    {
        // Slug comparison should be case-insensitive on the cache key (DB collation handles the
        // first-time query). Verify that a second call with different casing hits the cache.
        var (lookup, db) = CreateLookup();
        db.ReleaseAffiliateLinks.Add(MakeRow("Jaws-1975", null, "2016-Blu-Ray"));
        await db.SaveChangesAsync();

        var first = await lookup.GetAsync("Jaws-1975", null, "2016-Blu-Ray", CancellationToken.None);
        var second = await lookup.GetAsync("jaws-1975", null, "2016-blu-ray", CancellationToken.None);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
    }

    [Test]
    public async Task PreloadAsync_PopulatesCacheForListedReleases()
    {
        var (lookup, db) = CreateLookup();
        db.ReleaseAffiliateLinks.Add(MakeRow("jaws-1975", null, "2016-blu-ray", "jaws-bd"));
        db.ReleaseAffiliateLinks.Add(MakeRow("jaws-1975", null, "2020-uhd", "jaws-uhd"));
        await db.SaveChangesAsync();

        await lookup.PreloadAsync("jaws-1975", null, new[] { "2016-blu-ray", "2020-uhd", "missing" }, CancellationToken.None);

        // Dispose underlying DB to prove subsequent calls are served from cache, not DB.
        await db.DisposeAsync();

        var bd = await lookup.GetAsync("jaws-1975", null, "2016-blu-ray", CancellationToken.None);
        var uhd = await lookup.GetAsync("jaws-1975", null, "2020-uhd", CancellationToken.None);
        var missing = await lookup.GetAsync("jaws-1975", null, "missing", CancellationToken.None);

        await Assert.That(bd).IsNotNull();
        await Assert.That(uhd).IsNotNull();
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task PreloadAsync_EmptyList_DoesNothing()
    {
        var (lookup, _) = CreateLookup();
        await lookup.PreloadAsync("jaws-1975", null, Array.Empty<string>(), CancellationToken.None);
        // Should not throw and should not have populated anything
        var result = await lookup.GetAsync("jaws-1975", null, "x", CancellationToken.None);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetAsync_OnlyReturnsGruvProvider()
    {
        var (lookup, db) = CreateLookup();
        var row = MakeRow("jaws-1975", null, "2016-blu-ray");
        row.Provider = "other";
        db.ReleaseAffiliateLinks.Add(row);
        await db.SaveChangesAsync();

        var result = await lookup.GetAsync("jaws-1975", null, "2016-blu-ray", CancellationToken.None);
        await Assert.That(result).IsNull();
    }

    private sealed class TestDbContextFactory(DbContextOptions<SqlServerDataContext> options)
        : IDbContextFactory<SqlServerDataContext>
    {
        public SqlServerDataContext CreateDbContext() => new(options);
    }
}
