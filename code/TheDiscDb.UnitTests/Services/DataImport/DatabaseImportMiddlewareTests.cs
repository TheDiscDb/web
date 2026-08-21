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
