namespace TheDiscDb.UnitTests.Services.DataImport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public class DatabaseImportMiddlewareTests
{
    [Test]
    public async Task Process_SingleReleaseImports_PreservesAllNineReleases()
    {
        var factory = new InMemoryContextFactory();
        await using var middleware = CreateMiddleware(factory);

        for (int index = 1; index <= 9; index++)
        {
            await middleware.Process(CreateSeriesItem(index), default);
        }

        var update = CreateSeriesItem(5);
        update.MediaItem.Releases.Single().Title = "Updated Disc 005";
        await middleware.Process(update, default);

        await using var database = factory.CreateDbContext();
        var series = await database.MediaItems
            .Include(item => item.Releases)
                .ThenInclude(release => release.Discs)
                    .ThenInclude(link => link.Disc)
            .SingleAsync();

        await Assert.That(series.Releases.Count).IsEqualTo(9);
        for (int index = 1; index <= 9; index++)
        {
            var release = series.Releases.Single(item => item.Slug == $"2001-dvd-disc-{index:000}");
            await Assert.That(release.Title).IsEqualTo(index == 5 ? "Updated Disc 005" : $"Disc {index:000}");
            await Assert.That(release.Discs.Single().Disc!.ContentHash).IsEqualTo($"disc-hash-{index}");
        }
    }

    [Test]
    public async Task Process_ReorderedReleases_UpdatesBySlug()
    {
        var factory = new InMemoryContextFactory();
        await using var middleware = CreateMiddleware(factory);
        var original = CreateSeriesItem(1);
        original.MediaItem.Releases.Add(CreateSeriesItem(2).MediaItem.Releases.Single());
        await middleware.Process(original, default);

        var update = CreateSeriesItem(2);
        update.MediaItem.Releases.Single().Title = "Updated Disc 002";
        update.MediaItem.Releases.Add(CreateSeriesItem(1).MediaItem.Releases.Single());
        await middleware.Process(update, default);

        await using var database = factory.CreateDbContext();
        var releases = await database.Releases
            .Include(release => release.Discs)
                .ThenInclude(link => link.Disc)
            .ToListAsync();

        await Assert.That(releases.Count).IsEqualTo(2);
        var first = releases.Single(release => release.Slug == "2001-dvd-disc-001");
        var second = releases.Single(release => release.Slug == "2001-dvd-disc-002");
        await Assert.That(first.Title).IsEqualTo("Disc 001");
        await Assert.That(first.Discs.Single().Disc!.ContentHash).IsEqualTo("disc-hash-1");
        await Assert.That(second.Title).IsEqualTo("Updated Disc 002");
        await Assert.That(second.Discs.Single().Disc!.ContentHash).IsEqualTo("disc-hash-2");
    }

    [Test]
    public async Task Process_EmptyReleaseImport_PreservesExistingReleases()
    {
        var factory = new InMemoryContextFactory();
        await using var middleware = CreateMiddleware(factory);
        await middleware.Process(CreateSeriesItem(1), default);

        var update = CreateSeriesItem(1);
        update.MediaItem.Releases.Clear();
        await middleware.Process(update, default);

        await using var database = factory.CreateDbContext();
        var series = await database.MediaItems.Include(item => item.Releases).SingleAsync();
        await Assert.That(series.Releases.Count).IsEqualTo(1);
    }

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
    {
        var disc = new Disc
        {
            Format = "Blu-ray",
            ContentHash = contentHash,
        };
        var releaseDisc = new ReleaseDisc
        {
            Index = 1,
            Slug = "disc-1",
            Name = "Disc 1",
            GlobalDiscId = globalDiscId,
            Disc = disc,
        };
        var release = new Release
        {
            Slug = "shared-release",
            Title = "Shared Release",
            Year = 2026,
            Discs = [releaseDisc],
        };
        return new ImportItem
        {
            MediaItem = new MediaItem
            {
                Slug = slug,
                Title = slug,
                Type = "Movie",
                Year = 2026,
                Releases = [release],
            },
        };
    }

    private static ImportItem CreateSeriesItem(int index)
    {
        var item = CreateItem("super-dimension-fortress-macross-1982", $"disc-id-{index}", $"disc-hash-{index}");
        item.MediaItem.Type = "Series";
        item.MediaItem.Year = 1982;
        var release = item.MediaItem.Releases.Single();
        release.Slug = $"2001-dvd-disc-{index:000}";
        release.Title = $"Disc {index:000}";
        release.Year = 2001;
        release.Discs.Single().Disc!.Format = "DVD";
        return item;
    }

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
