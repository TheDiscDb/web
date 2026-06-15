namespace TheDiscDb.UnitTests.Data.Changes;

using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;
using EntityChapter = TheDiscDb.InputModels.Chapter;
using EntityTrack = TheDiscDb.InputModels.Track;

/// <summary>
/// Shared seed helpers for the disc / disc-item / chapter / track change-type
/// test suites. Builds a MediaItem → Release → Disc → Title (with Item.Chapters
/// and Tracks) graph using ONLY natural-key slugs; int ids are never named in
/// assertions because the whole point of the design is that they drift on
/// rebuild.
/// </summary>
internal static class ChangeTestSeed
{
    public const string MediaItemSlug = "the-movie";
    public const string ReleaseSlug = "the-release-slug";
    public const string DiscSlug = "disc-one";
    public const int DiscIndex = 0;
    public const int TitleIndex = 1;
    public const int ChapterIndex = 3;
    public const int TrackIndex = 2;

    public static SqlServerDataContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    public static (MediaItem MediaItem, Release Release, Disc Disc, Title Title, EntityChapter Chapter, EntityTrack Track) Seed(SqlServerDataContext db)
    {
        var chapter = new EntityChapter
        {
            Index = ChapterIndex,
            Title = "Original Chapter Title",
        };

        var itemRef = new DiscItemReference
        {
            Title = "Original Item Title",
            Type = "movie",
            Description = "Original description.",
            Season = "1",
            Episode = "3",
        };
        itemRef.Chapters.Add(chapter);

        var track = new EntityTrack
        {
            Index = TrackIndex,
            Name = "Original Track Name",
            Type = "Audio",
            AudioType = "DTS-HD MA",
            LanguageCode = "eng",
            Language = "English",
            Description = "Original track desc",
        };

        var title = new Title
        {
            Index = TitleIndex,
            Comment = "Original comment",
            SourceFile = "00001.mpls",
            SegmentMap = "1,2,3",
            Duration = "2:13:45",
            Item = itemRef,
        };
        title.Tracks.Add(track);

        var disc = new Disc
        {
            Slug = DiscSlug,
            Index = DiscIndex,
            Name = "Original Disc Name",
            Format = "Blu-ray",
        };
        disc.Titles.Add(title);

        var mediaItem = new MediaItem
        {
            Slug = MediaItemSlug,
            Title = "The Movie",
            FullTitle = "The Movie (2020)",
            Year = 2020,
            Type = "movie",
        };

        var release = new Release
        {
            Slug = ReleaseSlug,
            Title = "Original Release Title",
            RegionCode = "US",
            Locale = "en-US",
            Year = 2020,
            ReleaseDate = new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero),
            MediaItem = mediaItem,
        };
        release.Discs.Add(disc);
        mediaItem.Releases.Add(release);

        db.Add(mediaItem);
        db.SaveChanges();

        return (mediaItem, release, disc, title, chapter, track);
    }
}
