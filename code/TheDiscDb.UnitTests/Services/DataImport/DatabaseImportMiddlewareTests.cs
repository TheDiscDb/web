namespace TheDiscDb.UnitTests.Services.DataImport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public class DatabaseImportMiddlewareTests
{
    [Test]
    public async Task Process_MultiTitleDisc_KeepsOneGlobalDiscId()
    {
        var factory = new InMemoryContextFactory();
        await using var middleware = CreateMiddleware(factory);

        await middleware.Process(CreateItem("first-movie", "shared-disc-id", "shared-hash"), default);
        await middleware.Process(CreateItem("second-movie", "shared-disc-id", "shared-hash"), default);

        await using var database = factory.CreateDbContext();
        var links = await database.ReleaseDiscs
            .Include(link => link.Disc)
                .ThenInclude(disc => disc!.ReleaseDiscs)
            .OrderBy(link => link.Id)
            .ToListAsync();

        await Assert.That(links.Count).IsEqualTo(2);
        await Assert.That(links.Select(link => link.DiscId).Distinct().Count()).IsEqualTo(1);
        await Assert.That(links.Count(link => link.GlobalDiscId == "shared-disc-id")).IsEqualTo(1);
        await Assert.That(links.Count(link => link.GlobalDiscId == null)).IsEqualTo(1);
        await Assert.That(links.All(link => link.EffectiveGlobalDiscId() == "shared-disc-id")).IsTrue();
    }

    [Test]
    public async Task Process_GlobalDiscIdOnDifferentCanonicalDisc_Throws()
    {
        var factory = new InMemoryContextFactory();
        await using var middleware = CreateMiddleware(factory);
        await middleware.Process(CreateItem("first-movie", "conflicting-disc-id", "first-hash"), default);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.Process(
                CreateItem("second-movie", "conflicting-disc-id", "second-hash"),
                default));

        await Assert.That(exception!.Message).Contains("different canonical disc");
    }

    [Test]
    public async Task Process_EqualReleaseCountsWithChangedRelease_MatchesBySlug()
    {
        var factory = new InMemoryContextFactory();
        await using var middleware = CreateMiddleware(factory);

        await middleware.Process(
            CreateItem(
                "same-series",
                new ReleaseData("season-15", "season-15-id", "season-15-hash"),
                new ReleaseData("season-16", "season-16-id", "season-16-hash")),
            default);

        await middleware.Process(
            CreateItem(
                "same-series",
                new ReleaseData("season-20", "season-20-id", "season-20-hash"),
                new ReleaseData("season-15", "season-15-id", "season-15-hash")),
            default);

        await using var database = factory.CreateDbContext();
        var releases = await database.Releases
            .Include(release => release.Discs)
                .ThenInclude(releaseDisc => releaseDisc.Disc)
            .OrderBy(release => release.Slug)
            .ToListAsync();

        var season15Disc = releases.Single(release => release.Slug == "season-15").Discs.Single();
        await Assert.That(season15Disc.GlobalDiscId).IsEqualTo("season-15-id");
        await Assert.That(season15Disc.Disc!.ContentHash).IsEqualTo("season-15-hash");

        var season20Disc = releases.Single(release => release.Slug == "season-20").Discs.Single();
        await Assert.That(season20Disc.GlobalDiscId).IsEqualTo("season-20-id");
        await Assert.That(season20Disc.Disc!.ContentHash).IsEqualTo("season-20-hash");
    }

    private static DatabaseImportMiddleware CreateMiddleware(IDbContextFactory<SqlServerDataContext> factory)
    {
        var titleHandler = new TitleItemHandler(new TrackItemHandler(), new DiscItemReferenceItemHandler());
        var discHandler = new DiscItemHandler(titleHandler);
        var releaseHandler = new ReleaseItemHandler(new ReleaseDiscItemHandler(discHandler));
        return new DatabaseImportMiddleware(
            factory,
            new MediaItemHandler(releaseHandler),
            new BoxsetItemHandler(releaseHandler),
            discHandler,
            new ThrowingScopeFactory());
    }

    private static ImportItem CreateItem(string slug, string globalDiscId, string contentHash)
        => CreateItem(slug, new ReleaseData("shared-release", globalDiscId, contentHash));

    private static ImportItem CreateItem(string slug, params ReleaseData[] releases)
    {
        return new ImportItem
        {
            MediaItem = new MediaItem
            {
                Slug = slug,
                Title = slug,
                Type = "Movie",
                Year = 2026,
                Releases = releases.Select(release => new Release
                {
                    Slug = release.Slug,
                    Title = release.Slug,
                    Year = 2026,
                    Discs =
                    [
                        new ReleaseDisc
                        {
                            Index = 1,
                            Slug = "disc-1",
                            Name = "Disc 1",
                            GlobalDiscId = release.GlobalDiscId,
                            Disc = new Disc
                            {
                                Format = "Blu-ray",
                                ContentHash = release.ContentHash,
                            },
                        },
                    ],
                }).ToList(),
            },
        };
    }

    private sealed record ReleaseData(string Slug, string GlobalDiscId, string ContentHash);

    private sealed class InMemoryContextFactory : IDbContextFactory<SqlServerDataContext>
    {
        private readonly DbContextOptions<SqlServerDataContext> options =
            new DbContextOptionsBuilder<SqlServerDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

        public SqlServerDataContext CreateDbContext() => new(this.options);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("No contributor lookup was expected.");
    }
}
