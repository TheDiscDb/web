using HotChocolate;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.GraphQL;

public class TitleFileNameExtensionTests
{
    private static (TitleFileNameExtension Resolver, TestDbContextFactory Factory, Title Title) Setup(string itemType)
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);

        using (var seed = factory.CreateDbContext())
        {
            var media = new MediaItem
            {
                Title = "Inception",
                Year = 2010,
                FullTitle = "Inception (2010)",
                Type = "movie",
                Slug = "inception-2010",
                Externalids = new ExternalIds { Tmdb = "27205", Imdb = "tt1375666" },
            };
            var release = new Release
            {
                Slug = "2010-blu-ray",
                Title = "2010 Blu-ray",
                MediaItem = media,
            };
            var disc = new Disc
            {
                Slug = "disc-1",
                Index = 1,
                Name = "Disc 1",
                Format = "Blu-ray",
                Release = release,
            };
            var item = new DiscItemReference
            {
                Title = "Inception",
                Type = itemType,
            };
            var track = new Track
            {
                Index = 0,
                Type = "Video",
                Resolution = "1920x1080",
            };
            var title = new Title
            {
                Index = 1,
                SourceFile = "title_t01.mkv",
                Disc = disc,
                Item = item,
                Tracks = new List<Track> { track },
            };

            seed.MediaItems.Add(media);
            seed.Titles.Add(title);
            seed.SaveChanges();
        }

        using var read = factory.CreateDbContext();
        var loaded = read.Titles.AsNoTracking().First();
        return (new TitleFileNameExtension(), factory, loaded);
    }

    [Test]
    public async Task GetFilename_NoTemplates_UsesDefault()
    {
        var (resolver, factory, title) = Setup("MainMovie");

        var actual = await resolver.GetFilename(title, templates: null, factory, CancellationToken.None);

        await Assert.That(actual).IsEqualTo("Inception (2010) [1080p].mkv");
    }

    [Test]
    public async Task GetFilename_WithOverride_UsesProvidedTemplate()
    {
        var (resolver, factory, title) = Setup("MainMovie");

        var templates = new[]
        {
            new FileNameTemplateInput { ItemType = "MainMovie", Template = "{title} [{tmdbid}]" },
        };

        var actual = await resolver.GetFilename(title, templates, factory, CancellationToken.None);

        await Assert.That(actual).IsEqualTo("Inception [27205]");
    }

    [Test]
    public async Task GetFilename_OverrideForDifferentType_FallsBackToDefault()
    {
        var (resolver, factory, title) = Setup("MainMovie");

        var templates = new[]
        {
            new FileNameTemplateInput { ItemType = "Episode", Template = "{title}" },
        };

        var actual = await resolver.GetFilename(title, templates, factory, CancellationToken.None);

        await Assert.That(actual).IsEqualTo("Inception (2010) [1080p].mkv");
    }

    [Test]
    public async Task GetFilename_InvalidTemplate_Throws()
    {
        var (resolver, factory, title) = Setup("MainMovie");

        var templates = new[]
        {
            new FileNameTemplateInput { ItemType = "MainMovie", Template = "{nonexistenttoken}" },
        };

        await Assert.ThrowsAsync<GraphQLException>(async () =>
            await resolver.GetFilename(title, templates, factory, CancellationToken.None));
    }

    [Test]
    public async Task GetFilename_UnknownItemType_Throws()
    {
        var (resolver, factory, title) = Setup("MainMovie");

        var templates = new[]
        {
            new FileNameTemplateInput { ItemType = "Bogus", Template = "{title}" },
        };

        await Assert.ThrowsAsync<GraphQLException>(async () =>
            await resolver.GetFilename(title, templates, factory, CancellationToken.None));
    }

    [Test]
    public async Task GetFilename_EmptyTemplate_Throws()
    {
        var (resolver, factory, title) = Setup("MainMovie");

        var templates = new[]
        {
            new FileNameTemplateInput { ItemType = "MainMovie", Template = "" },
        };

        await Assert.ThrowsAsync<GraphQLException>(async () =>
            await resolver.GetFilename(title, templates, factory, CancellationToken.None));
    }

    [Test]
    public async Task GetFilename_TitleHasNoItem_ReturnsEmpty()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);

        using (var seed = factory.CreateDbContext())
        {
            var media = new MediaItem { Title = "X", Year = 2020, Type = "movie", Slug = "x-2020" };
            var release = new Release { Slug = "r", Title = "R", MediaItem = media };
            var disc = new Disc { Slug = "d", Index = 1, Name = "D", Format = "Blu-ray", Release = release };
            var title = new Title { Index = 1, SourceFile = "x.mkv", Disc = disc };

            seed.MediaItems.Add(media);
            seed.Titles.Add(title);
            seed.SaveChanges();
        }

        using var read = factory.CreateDbContext();
        var loaded = read.Titles.AsNoTracking().First();
        var resolver = new TitleFileNameExtension();

        var actual = await resolver.GetFilename(loaded, templates: null, factory, CancellationToken.None);

        await Assert.That(actual).IsEqualTo(string.Empty);
    }

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
}
