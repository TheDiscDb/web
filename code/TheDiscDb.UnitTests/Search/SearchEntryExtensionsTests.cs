using TheDiscDb.InputModels;
using TheDiscDb.Search;

namespace TheDiscDb.UnitTests.Search;

public class SearchEntryExtensionsTests
{
    [Test]
    public async Task ToSearchEntries_MediaItem_ProducesCorrectRootEntry()
    {
        var item = CreateMediaItem("Movie", "the-matrix", "The Matrix");

        var entries = item.ToSearchEntries().ToList();
        var rootEntry = entries.First();

        await Assert.That(rootEntry.Type).IsEqualTo("Movie");
        await Assert.That(rootEntry.Title).IsEqualTo("The Matrix");
        await Assert.That(rootEntry.RelativeUrl).IsEqualTo("/movie/the-matrix");
        await Assert.That(rootEntry.MediaItem!.Slug).IsEqualTo("the-matrix");
    }

    [Test]
    public async Task ToSearchEntries_MediaItemWithRelease_IncludesReleaseEntries()
    {
        var item = CreateMediaItem("Movie", "the-matrix", "The Matrix");
        var release = new Release { Slug = "4k-edition", Title = "4K Edition", ImageUrl = "img.jpg" };
        item.Releases.Add(release);

        var entries = item.ToSearchEntries().ToList();
        var releaseEntry = entries.FirstOrDefault(e => e.Type == "Release");

        await Assert.That(releaseEntry).IsNotNull();
        await Assert.That(releaseEntry!.Title).IsEqualTo("4K Edition");
        await Assert.That(releaseEntry.RelativeUrl).IsEqualTo("/movie/the-matrix/releases/4k-edition");
        await Assert.That(releaseEntry.Release!.Slug).IsEqualTo("4k-edition");
    }

    [Test]
    public async Task ToSearchEntries_MediaItemWithDiscs_IncludesDiscEntries()
    {
        var item = CreateMediaItem("Movie", "the-matrix", "The Matrix");
        var release = new Release { Slug = "4k-edition", Title = "4K Edition", ImageUrl = "img.jpg" };
        var disc = new Disc { Id = 1, Index = 1, Name = "Feature Disc", Slug = "feature" };
        release.Discs.Add(disc);
        item.Releases.Add(release);

        var entries = item.ToSearchEntries().ToList();
        var discEntry = entries.FirstOrDefault(e => e.Type == "Disc");

        await Assert.That(discEntry).IsNotNull();
        await Assert.That(discEntry!.Title).IsEqualTo("Feature Disc");
        await Assert.That(discEntry.Disc!.Slug).IsEqualTo("feature");
    }

    [Test]
    public async Task ToSearchEntries_MediaItemWithNoReleases_ReturnsSingleEntry()
    {
        var item = CreateMediaItem("Movie", "no-releases", "No Releases");

        var entries = item.ToSearchEntries().ToList();

        await Assert.That(entries).HasCount().EqualTo(1);
    }

    [Test]
    public async Task ToSearchEntries_Boxset_ProducesCorrectRootEntry()
    {
        var boxset = new Boxset
        {
            Id = 1,
            Title = "Lord of the Rings Collection",
            Slug = "lotr-collection",
            ImageUrl = "lotr.jpg"
        };

        var entries = boxset.ToSearchEntries().ToList();
        var rootEntry = entries.First();

        await Assert.That(rootEntry.Type).IsEqualTo("Boxset");
        await Assert.That(rootEntry.Title).IsEqualTo("Lord of the Rings Collection");
        await Assert.That(rootEntry.RelativeUrl).IsEqualTo("/boxset/lotr-collection");
    }

    [Test]
    public async Task ToSearchEntries_BoxsetWithNullRelease_ReturnsSingleEntry()
    {
        var boxset = new Boxset
        {
            Id = 1,
            Title = "Empty Boxset",
            Slug = "empty",
            ImageUrl = "img.jpg",
            Release = null
        };

        var entries = boxset.ToSearchEntries().ToList();

        await Assert.That(entries).HasCount().EqualTo(1);
    }

    [Test]
    public async Task ToSearchEntries_BoxsetWithDiscs_IncludesDiscEntries()
    {
        var boxset = new Boxset
        {
            Id = 1,
            Title = "Collection",
            Slug = "collection",
            ImageUrl = "img.jpg",
            Release = new Release
            {
                Slug = "box-release",
                ImageUrl = "release.jpg"
            }
        };
        boxset.Release.Discs.Add(new Disc { Id = 10, Index = 1, Name = "Disc 1", Slug = "disc-1" });

        var entries = boxset.ToSearchEntries().ToList();
        var discEntry = entries.FirstOrDefault(e => e.Type == "BoxsetDisc");

        await Assert.That(discEntry).IsNotNull();
        await Assert.That(discEntry!.Title).IsEqualTo("Disc 1");
    }

    [Test]
    public async Task ToSearchEntries_MediaItem_IdFormat_IsTypeHyphenSlug()
    {
        var item = CreateMediaItem("Movie", "test-slug", "Test");

        var entries = item.ToSearchEntries().ToList();

        await Assert.That(entries.First().id).IsEqualTo("movie-test-slug");
    }

    private static MediaItem CreateMediaItem(string type, string slug, string title)
    {
        return new Movie
        {
            Type = type,
            Slug = slug,
            Title = title,
            ImageUrl = $"{slug}.jpg"
        };
    }
}
