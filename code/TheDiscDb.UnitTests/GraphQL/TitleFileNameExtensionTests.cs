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
            media.Releases.Add(release);
            var disc = new Disc
            {
                Format = "Blu-ray",
            };
            release.Discs.Add(new ReleaseDisc
            {
                Slug = "disc-1",
                Index = 1,
                Name = "Disc 1",
                Disc = disc,
            });
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
            media.Releases.Add(release);
            var disc = new Disc { Format = "Blu-ray" };
            release.Discs.Add(new ReleaseDisc { Slug = "d", Index = 1, Name = "D", Disc = disc });
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

    [Test]
    public async Task GetFilename_BoxsetDisc_ResolvesSourceMovieMetadata()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);

        // Mirror real boxset import: the source movie has a release with the same
        // slug as the boxset's release, containing a disc with the same slug as the
        // boxset's copy of that disc.
        const string releaseSlug = "the-wes-anderson-archive-criterion-4k";
        const string discSlug = "bottle-rocket-blu-ray";

        using (var seed = factory.CreateDbContext())
        {
            var movie = new MediaItem
            {
                Title = "Bottle Rocket",
                Year = 1996,
                Type = "movie",
                Slug = "bottle-rocket-1996",
                Externalids = new ExternalIds { Tmdb = "9428", Imdb = "tt0115734" },
            };
            var movieRelease = new Release
            {
                Slug = releaseSlug,
                Title = "The Wes Anderson Archive",
                MediaItem = movie,
            };
            movie.Releases.Add(movieRelease);
            var movieDisc = new Disc
            {
                Format = "Blu-ray",
            };
            movieRelease.Discs.Add(new ReleaseDisc
            {
                Slug = discSlug,
                Index = 1,
                Name = "Bottle Rocket Blu-ray",
                Disc = movieDisc,
            });
            seed.MediaItems.Add(movie);
            seed.Discs.Add(movieDisc);

            var boxset = new Boxset
            {
                Title = "The Wes Anderson Archive",
                Slug = "the-wes-anderson-archive-criterion-4k",
            };
            var boxsetRelease = new Release
            {
                Slug = releaseSlug,
                Title = "The Wes Anderson Archive",
                Year = 2025,
                Boxset = boxset,
            };
            boxset.Release = boxsetRelease;
            var boxsetDisc = new Disc
            {
                Format = "Blu-ray",
            };
            boxsetRelease.Discs.Add(new ReleaseDisc
            {
                Slug = discSlug,
                Index = 1,
                Name = "Bottle Rocket Blu-ray",
                Disc = boxsetDisc,
            });
            var item = new DiscItemReference { Title = "Bottle Rocket", Type = "MainMovie" };
            var track = new Track { Index = 0, Type = "Video", Resolution = "1920x1080" };
            var title = new Title
            {
                Index = 1,
                SourceFile = "title_t01.mkv",
                Disc = boxsetDisc,
                Item = item,
                Tracks = new List<Track> { track },
            };

            seed.BoxSets.Add(boxset);
            seed.Titles.Add(title);
            seed.SaveChanges();
        }

        using var read = factory.CreateDbContext();
        // Pick the title attached to the boxset disc (the one with an Item).
        var loaded = read.Titles.AsNoTracking()
            .Include(t => t.Item)
            .First(t => t.Item != null);
        var resolver = new TitleFileNameExtension();

        var actual = await resolver.GetFilename(loaded, templates: null, factory, CancellationToken.None);

        await Assert.That(actual).IsEqualTo("Bottle Rocket (1996) [1080p].mkv");
    }

    [Test]
    public async Task GetFilename_BoxsetDisc_NoSourceRelease_FallsBackToBoxsetMetadata()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = new TestDbContextFactory(dbName);

        using (var seed = factory.CreateDbContext())
        {
            var boxset = new Boxset { Title = "Custom Set", Slug = "custom-set" };
            var release = new Release { Slug = "custom-set-r1", Title = "Custom Set", Year = 2024, Boxset = boxset };
            boxset.Release = release;
            var disc = new Disc { Format = "Blu-ray" };
            release.Discs.Add(new ReleaseDisc { Slug = "orphan-disc", Index = 1, Name = "Orphan", Disc = disc });
            var item = new DiscItemReference { Title = "Some Feature", Type = "MainMovie" };
            var track = new Track { Index = 0, Type = "Video", Resolution = "1920x1080" };
            var title = new Title
            {
                Index = 1,
                SourceFile = "title_t01.mkv",
                Disc = disc,
                Item = item,
                Tracks = new List<Track> { track },
            };

            seed.BoxSets.Add(boxset);
            seed.Titles.Add(title);
            seed.SaveChanges();
        }

        using var read = factory.CreateDbContext();
        var loaded = read.Titles.AsNoTracking().Include(t => t.Item).First();
        var resolver = new TitleFileNameExtension();

        var actual = await resolver.GetFilename(loaded, templates: null, factory, CancellationToken.None);

        // Falls back to boxset metadata when no source release matches.
        await Assert.That(actual).IsEqualTo("Custom Set (2024) [1080p].mkv");
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
