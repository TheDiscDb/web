namespace TheDiscDb.UnitTests.Services.DiscLookup;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Services.DiscLookup;
using TheDiscDb.UnitTests.Data.Changes;

public class DiscLookupQueryTests
{
    private const string AacsId = "A734E4BEE726B8943F2E8817E3956EFC5F786C8B";
    private const string DvdId = "91C2EB717C4323D8807C01BA79011A6B";

    [Test]
    public async Task LookupAsync_KnownAacsId_ReturnsDiscWithTitlesAndItem()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.ReleaseDisc.GlobalDiscId = AacsId;
        seed.Disc.ContentHash = "AAAA1111BBBB2222CCCC3333DDDD4444";
        await db.SaveChangesAsync();

        var result = await DiscLookupQuery.LookupAsync(db, AacsId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GlobalDiscId).IsEqualTo(AacsId);
        await Assert.That(result.Format).IsEqualTo("Blu-ray");
        await Assert.That(result.ContentHash).IsEqualTo("AAAA1111BBBB2222CCCC3333DDDD4444");
        await Assert.That(result.Titles.Count).IsEqualTo(1);

        var title = result.Titles[0];
        await Assert.That(title.Index).IsEqualTo(ChangeTestSeed.TitleIndex);
        await Assert.That(title.SourceFile).IsEqualTo("00001.mpls");
        await Assert.That(title.SegmentMap).IsEqualTo("1,2,3");
        await Assert.That(title.Item).IsNotNull();
        await Assert.That(title.Item!.Title).IsEqualTo("Original Item Title");
        await Assert.That(title.Item.Type).IsEqualTo("movie");
        await Assert.That(title.Item.Season).IsEqualTo("1");
        await Assert.That(title.Item.Episode).IsEqualTo("3");
    }

    [Test]
    public async Task LookupAsync_IsCaseInsensitive()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.ReleaseDisc.GlobalDiscId = AacsId;
        await db.SaveChangesAsync();

        var result = await DiscLookupQuery.LookupAsync(db, AacsId.ToLowerInvariant());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GlobalDiscId).IsEqualTo(AacsId);
    }

    [Test]
    public async Task LookupAsync_UnknownId_ReturnsNull()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);
        await db.SaveChangesAsync();

        var result = await DiscLookupQuery.LookupAsync(db, DvdId);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task LookupAsync_DvdId_Resolves()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.ReleaseDisc.GlobalDiscId = DvdId;
        seed.Disc.Format = "DVD";
        await db.SaveChangesAsync();

        var result = await DiscLookupQuery.LookupAsync(db, DvdId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Format).IsEqualTo("DVD");
    }

    [Test]
    public async Task LookupByDiscHashAsync_ReturnsSameCompleteNamingPayload()
    {
        const string contentHash = "AAAA1111BBBB2222CCCC3333DDDD4444";

        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.ReleaseDisc.GlobalDiscId = AacsId;
        seed.Disc.ContentHash = contentHash;
        seed.Disc.Format = "UHD";
        seed.Title.Item!.Type = "MainMovie";
        seed.Title.Item.Chapters.Add(new Chapter { Index = 1, Title = "Opening" });
        seed.Title.Tracks.Add(new Track
        {
            Index = 0,
            Name = "Main Video",
            Type = "Video",
            Resolution = "3840x2160",
            AspectRatio = "16:9",
            Description = "HEVC video",
        });
        seed.Title.Tracks.Add(new Track
        {
            Index = 4,
            Name = "English Forced",
            Type = "Subtitles",
            LanguageCode = "eng",
            Language = "English",
            Description = "Forced subtitles",
        });
        seed.Track.Name = "English Atmos";
        seed.Track.Resolution = "48 kHz";
        seed.Track.AspectRatio = null;
        seed.Release.Upc = "123456789012";
        seed.MediaItem.Externalids.Tmdb = "123";
        seed.MediaItem.Externalids.Imdb = "tt0000123";
        await db.SaveChangesAsync();

        var byDiscId = await DiscLookupQuery.LookupAsync(db, AacsId);
        var byHash = await DiscLookupQuery.LookupByDiscHashAsync(
            db,
            contentHash.ToLowerInvariant());

        await Assert.That(byHash).IsNotNull();
        await Assert.That(byHash!.GlobalDiscId).IsEqualTo(AacsId);
        await Assert.That(byHash.Format).IsEqualTo(byDiscId!.Format);
        await Assert.That(byHash.ContentHash).IsEqualTo(byDiscId.ContentHash);
        await Assert.That(byHash.Media!.Title).IsEqualTo("The Movie");
        await Assert.That(byHash.Media.FullTitle).IsEqualTo("The Movie (2020)");
        await Assert.That(byHash.Media.Year).IsEqualTo(2020);
        await Assert.That(byHash.Media.Type).IsEqualTo("movie");
        await Assert.That(byHash.Media.ExternalIds.Tmdb).IsEqualTo("123");
        await Assert.That(byHash.Media.ExternalIds.Imdb).IsEqualTo("tt0000123");
        await Assert.That(byHash.Release!.Slug).IsEqualTo(ChangeTestSeed.ReleaseSlug);
        await Assert.That(byHash.Release.Title).IsEqualTo("Original Release Title");
        await Assert.That(byHash.Release.RegionCode).IsEqualTo("US");
        await Assert.That(byHash.Release.Locale).IsEqualTo("en-US");
        await Assert.That(byHash.Release.Upc).IsEqualTo("123456789012");
        await Assert.That(byHash.Disc!.Slug).IsEqualTo(ChangeTestSeed.DiscSlug);
        await Assert.That(byHash.Disc.Name).IsEqualTo("Original Disc Name");
        await Assert.That(byHash.Disc.Index).IsEqualTo(ChangeTestSeed.DiscIndex);

        var title = byHash.Titles[0];
        await Assert.That(title.FileName).IsEqualTo("The Movie (2020) [2160p].mkv");
        await Assert.That(title.Resolution).IsEqualTo("2160p");
        await Assert.That(title.Item!.Description).IsEqualTo("Original description.");
        await Assert.That(title.Chapters[0].Index).IsEqualTo(1);
        await Assert.That(title.Chapters[1].Index).IsEqualTo(3);
        await Assert.That(title.Chapters[0].Title).IsEqualTo("Opening");
        await Assert.That(title.Tracks[0].Index).IsEqualTo(0);
        await Assert.That(title.Tracks[1].Index).IsEqualTo(2);
        await Assert.That(title.Tracks[2].Index).IsEqualTo(4);

        var video = title.Tracks[0];
        await Assert.That(video.Name).IsEqualTo("Main Video");
        await Assert.That(video.Type).IsEqualTo("Video");
        await Assert.That(video.Resolution).IsEqualTo("3840x2160");
        await Assert.That(video.AspectRatio).IsEqualTo("16:9");
        await Assert.That(video.Description).IsEqualTo("HEVC video");

        var audio = title.Tracks[1];
        await Assert.That(audio.Name).IsEqualTo("English Atmos");
        await Assert.That(audio.AudioType).IsEqualTo("DTS-HD MA");
        await Assert.That(audio.LanguageCode).IsEqualTo("eng");
        await Assert.That(audio.Language).IsEqualTo("English");
        await Assert.That(audio.Description).IsEqualTo("Original track desc");

        var subtitle = title.Tracks[2];
        await Assert.That(subtitle.Name).IsEqualTo("English Forced");
        await Assert.That(subtitle.Type).IsEqualTo("Subtitles");
        await Assert.That(subtitle.LanguageCode).IsEqualTo("eng");
        await Assert.That(subtitle.Language).IsEqualTo("English");
        await Assert.That(subtitle.Description).IsEqualTo("Forced subtitles");
    }

    [Test]
    public async Task LookupAllAsync_SharedPressing_FindsBothReleases()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.ReleaseDisc.GlobalDiscId = AacsId;   // standalone release stores the id
        seed.Disc.ContentHash = "AAAA1111BBBB2222CCCC3333DDDD4444";

        // A second release (e.g. a boxset copy via .ref) whose release-disc shares the SAME canonical
        // disc but stores no id of its own — it should be found via the shared-pressing fallback.
        var otherRelease = new TheDiscDb.InputModels.Release
        {
            Slug = "boxset-release",
            Title = "Boxset Release",
            Year = 2021,
            MediaItem = seed.MediaItem,
        };
        otherRelease.Discs.Add(new TheDiscDb.InputModels.ReleaseDisc
        {
            Slug = ChangeTestSeed.DiscSlug,
            Index = ChangeTestSeed.DiscIndex,
            Name = "Original Disc Name",
            Disc = seed.Disc,          // same canonical disc
            GlobalDiscId = null,       // no own id -> relies on fallback
        });
        seed.MediaItem.Releases.Add(otherRelease);
        await db.SaveChangesAsync();

        var results = await DiscLookupQuery.LookupAllAsync(db, AacsId);

        await Assert.That(results.Count).IsEqualTo(2);
        // Both report the same (effective) Disc ID.
        await Assert.That(results.All(r => r.GlobalDiscId == AacsId)).IsTrue();
        // Both releases are represented.
        var releaseSlugs = results.Select(r => r.Release!.Slug).ToList();
        await Assert.That(releaseSlugs).Contains(ChangeTestSeed.ReleaseSlug);
        await Assert.That(releaseSlugs).Contains("boxset-release");
    }

    [Test]
    public async Task LookupAllAsync_Collision_ExcludesReleaseDiscWithDifferentId()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.ReleaseDisc.GlobalDiscId = AacsId;
        seed.Disc.ContentHash = "AAAA1111BBBB2222CCCC3333DDDD4444";

        // A re-press: same content (same canonical disc) but a DIFFERENT Disc ID.
        var otherRelease = new TheDiscDb.InputModels.Release
        {
            Slug = "repress-release",
            Title = "Re-press Release",
            Year = 2022,
            MediaItem = seed.MediaItem,
        };
        otherRelease.Discs.Add(new TheDiscDb.InputModels.ReleaseDisc
        {
            Slug = ChangeTestSeed.DiscSlug,
            Index = ChangeTestSeed.DiscIndex,
            Name = "Original Disc Name",
            Disc = seed.Disc,
            GlobalDiscId = DvdId,      // different id -> a distinct pressing
        });
        seed.MediaItem.Releases.Add(otherRelease);
        await db.SaveChangesAsync();

        // Querying one id returns only its own release-disc, never the re-press.
        var results = await DiscLookupQuery.LookupAllAsync(db, AacsId);

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results[0].Release!.Slug).IsEqualTo(ChangeTestSeed.ReleaseSlug);
    }

    [Test]
    public async Task IsValidDiscId_ValidatesHexLengths()
    {
        await Assert.That(DiscLookupQuery.IsValidDiscId(AacsId)).IsTrue();     // 40 hex
        await Assert.That(DiscLookupQuery.IsValidDiscId(DvdId)).IsTrue();      // 32 hex
        await Assert.That(DiscLookupQuery.IsValidDiscId(AacsId.ToLowerInvariant())).IsTrue();
        await Assert.That(DiscLookupQuery.IsValidDiscId("not-hex")).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscId("")).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscId(null)).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscId(AacsId + "AA")).IsFalse(); // 42
    }

    [Test]
    public async Task IsValidDiscHash_Requires32HexCharacters()
    {
        await Assert.That(DiscLookupQuery.IsValidDiscHash(DvdId)).IsTrue();
        await Assert.That(DiscLookupQuery.IsValidDiscHash(DvdId.ToLowerInvariant())).IsTrue();
        await Assert.That(DiscLookupQuery.IsValidDiscHash(AacsId)).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscHash("not-hex")).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscHash("")).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscHash(null)).IsFalse();
    }
}
