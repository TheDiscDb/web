using IMDbApiLib.Models;
using TheDiscDb.Data.Import;
using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.ImportModels;
using TheDiscDb.InputModels;

namespace TheDiscDb.UnitTests.DataImport;

public class GroupMiddlewareTests
{
    [Test]
    public async Task Process_WithImdbGenres_CreatesMediaItemGroups()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem(genreList: [new KeyValueItem { Key = "Action", Value = "Action" }, new KeyValueItem { Key = "Drama", Value = "Drama" }]);

        await middleware.Process(item, CancellationToken.None);

        var groups = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Genre).ToList();
        await Assert.That(groups).Count().IsEqualTo(2);
        await Assert.That(groups[0].Group!.Name).IsEqualTo("Action");
        await Assert.That(groups[0].Group!.Slug).IsEqualTo("action");
        await Assert.That(groups[1].Group!.Name).IsEqualTo("Drama");
    }

    [Test]
    public async Task Process_WithImdbDirectors_CreatesFeaturedGroups()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem(directorList: [new StarShort { Id = "nm001", Name = "Steven Spielberg" }]);

        await middleware.Process(item, CancellationToken.None);

        var groups = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Director).ToList();
        await Assert.That(groups).Count().IsEqualTo(1);
        await Assert.That(groups[0].Group!.Name).IsEqualTo("Steven Spielberg");
        await Assert.That(groups[0].Group!.Slug).IsEqualTo("steven-spielberg");
        await Assert.That(groups[0].IsFeatured).IsTrue();
    }

    [Test]
    public async Task Process_WithImdbActors_CreatesActorGroups()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem(starList: [new StarShort { Id = "nm002", Name = "Tom Hanks" }]);

        await middleware.Process(item, CancellationToken.None);

        var groups = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Actor).ToList();
        await Assert.That(groups).Count().IsEqualTo(1);
        await Assert.That(groups[0].Group!.Name).IsEqualTo("Tom Hanks");
        await Assert.That(groups[0].IsFeatured).IsFalse();
    }

    [Test]
    public async Task Process_WithImdbWriters_CreatesWriterGroups()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem(writerList: [new StarShort { Id = "nm003", Name = "Aaron Sorkin" }]);

        await middleware.Process(item, CancellationToken.None);

        var groups = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Writer).ToList();
        await Assert.That(groups).Count().IsEqualTo(1);
        await Assert.That(groups[0].Group!.Name).IsEqualTo("Aaron Sorkin");
    }

    [Test]
    public async Task Process_WithCustomGroups_CreatesCustomGroupRole()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem();
        item.Metadata = new MetadataFile { Groups = ["4K Collection", "Steelbook"] };

        await middleware.Process(item, CancellationToken.None);

        var groups = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.CustomGroup).ToList();
        await Assert.That(groups).Count().IsEqualTo(2);
        await Assert.That(groups[0].Group!.Name).IsEqualTo("4K Collection");
        await Assert.That(groups[1].Group!.Name).IsEqualTo("Steelbook");
    }

    [Test]
    public async Task Process_WithImdbAndCustomGroups_CreatesBoth()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem(genreList: [new KeyValueItem { Key = "Action", Value = "Action" }]);
        item.Metadata = new MetadataFile { Groups = ["Steelbook"] };

        await middleware.Process(item, CancellationToken.None);

        // IMDB genres are added via TryAddGroups which also calls TryAddCustomGroups,
        // but the middleware adds both independently
        var genres = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Genre).ToList();
        var custom = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.CustomGroup).ToList();
        await Assert.That(genres).Count().IsEqualTo(1);
        await Assert.That(custom).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Process_DuplicateGenres_NoDuplicateGroups()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem(genreList: [
            new KeyValueItem { Key = "Action", Value = "Action" },
            new KeyValueItem { Key = "Action", Value = "Action" }
        ]);

        await middleware.Process(item, CancellationToken.None);

        var groups = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Genre).ToList();
        await Assert.That(groups).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Process_SamePersonMultipleRoles_CreatesSeparateGroups()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem(
            directorList: [new StarShort { Id = "nm001", Name = "Clint Eastwood" }],
            starList: [new StarShort { Id = "nm001", Name = "Clint Eastwood" }]
        );

        await middleware.Process(item, CancellationToken.None);

        var directors = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Director).ToList();
        var actors = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Actor).ToList();
        await Assert.That(directors).Count().IsEqualTo(1);
        await Assert.That(actors).Count().IsEqualTo(1);
        // Both should share the same Group instance (same imdbId)
        await Assert.That(directors[0].Group).IsSameReferenceAs(actors[0].Group);
    }

    [Test]
    public async Task Process_WithTmdbOnly_CreatesGroupsFromTmdb()
    {
        var middleware = new GroupMiddleware();
        var item = new ImportItem
        {
            MediaItem = new Movie { Title = "Test", Slug = "test" },
            Metadata = new MetadataFile(),
            TmdbData = new TmdbMetadata
            {
                GenreList = ["Sci-Fi", "Thriller"],
                DirectorList = ["Denis Villeneuve"],
                WriterList = [],
                StarList = ["Timothée Chalamet"]
            }
        };

        await middleware.Process(item, CancellationToken.None);

        var genres = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Genre).ToList();
        var directors = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Director).ToList();
        var actors = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Actor).ToList();
        await Assert.That(genres).Count().IsEqualTo(2);
        await Assert.That(directors).Count().IsEqualTo(1);
        await Assert.That(directors[0].IsFeatured).IsTrue();
        await Assert.That(actors).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Process_WithImdbAndTmdb_PrefersImdb()
    {
        var middleware = new GroupMiddleware();
        var item = CreateImportItem(genreList: [new KeyValueItem { Key = "Action", Value = "Action" }]);
        item.TmdbData = new TmdbMetadata
        {
            GenreList = ["Sci-Fi", "Thriller"],
            DirectorList = [],
            WriterList = [],
            StarList = []
        };

        await middleware.Process(item, CancellationToken.None);

        // Should have only the IMDB genre, not TMDB genres
        var genres = item.MediaItem.MediaItemGroups.Where(g => g.Role == Roles.Genre).ToList();
        await Assert.That(genres).Count().IsEqualTo(1);
        await Assert.That(genres[0].Group!.Name).IsEqualTo("Action");
    }

    [Test]
    public async Task Process_NullMediaItem_DoesNotThrow()
    {
        var middleware = new GroupMiddleware();
        var item = new ImportItem { Boxset = new Boxset() };

        await middleware.Process(item, CancellationToken.None);
        // Should not throw — boxsets skip group processing
    }

    [Test]
    public async Task Process_CallsNext()
    {
        var nextCalled = false;
        var middleware = new GroupMiddleware();
        middleware.Next = (_, _) => { nextCalled = true; return Task.CompletedTask; };

        var item = CreateImportItem();
        await middleware.Process(item, CancellationToken.None);

        await Assert.That(nextCalled).IsTrue();
    }

    [Test]
    public async Task Process_GroupsCachedAcrossItems()
    {
        var middleware = new GroupMiddleware();
        var item1 = CreateImportItem(genreList: [new KeyValueItem { Key = "Action", Value = "Action" }]);
        var item2 = CreateImportItem(genreList: [new KeyValueItem { Key = "Action", Value = "Action" }]);

        await middleware.Process(item1, CancellationToken.None);
        await middleware.Process(item2, CancellationToken.None);

        var group1 = item1.MediaItem.MediaItemGroups.First().Group;
        var group2 = item2.MediaItem.MediaItemGroups.First().Group;
        await Assert.That(group1).IsSameReferenceAs(group2);
    }

    private static ImportItem CreateImportItem(
        List<KeyValueItem>? genreList = null,
        List<StarShort>? directorList = null,
        List<StarShort>? writerList = null,
        List<StarShort>? starList = null)
    {
        var imdb = new TitleData
        {
            GenreList = genreList ?? [],
            DirectorList = directorList ?? [],
            WriterList = writerList ?? [],
            StarList = starList ?? [],
            CompanyList = []
        };

        return new ImportItem
        {
            MediaItem = new Movie { Title = "Test Movie", Slug = "test-movie" },
            ImdbData = imdb,
            Metadata = new MetadataFile()
        };
    }
}
