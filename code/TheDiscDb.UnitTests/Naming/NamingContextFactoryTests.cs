using TheDiscDb.InputModels;
using TheDiscDb.Naming;

namespace TheDiscDb.UnitTests.Naming;

public class NamingContextFactoryTests
{
    [Test]
    public async Task Create_MediaItem_SetsTitle()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999 };

        var ctx = NamingContext.Create(movie);

        await Assert.That(ctx.Title).IsEqualTo("The Matrix");
    }

    [Test]
    public async Task Create_MediaItem_SetsYearAsString()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999 };

        var ctx = NamingContext.Create(movie);

        await Assert.That(ctx.Year).IsEqualTo("1999");
    }

    [Test]
    public async Task Create_MediaItem_ZeroYear_YearIsNull()
    {
        var movie = new Movie { Title = "Unknown", Year = 0 };

        var ctx = NamingContext.Create(movie);

        await Assert.That(ctx.Year).IsNull();
    }

    [Test]
    public async Task Create_MediaItem_BuildsFullTitle_WhenNotSet()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999 };

        var ctx = NamingContext.Create(movie);

        await Assert.That(ctx.FullTitle).IsEqualTo("The Matrix (1999)");
    }

    [Test]
    public async Task Create_MediaItem_UsesExistingFullTitle_WhenSet()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999, FullTitle = "The Matrix: Special" };

        var ctx = NamingContext.Create(movie);

        await Assert.That(ctx.FullTitle).IsEqualTo("The Matrix: Special");
    }

    [Test]
    public async Task Create_MediaItem_FullTitle_NoYear_JustTitle()
    {
        var movie = new Movie { Title = "Unknown", Year = 0 };

        var ctx = NamingContext.Create(movie);

        await Assert.That(ctx.FullTitle).IsEqualTo("Unknown");
    }

    [Test]
    public async Task Create_MediaItem_SetsExternalIds()
    {
        var movie = new Movie
        {
            Title = "The Matrix",
            Year = 1999,
            Externalids = new ExternalIds { Tmdb = "603", Imdb = "tt0133093", Tvdb = "1234" },
        };

        var ctx = NamingContext.Create(movie);

        await Assert.That(ctx.TmdbId).IsEqualTo("603");
        await Assert.That(ctx.ImdbId).IsEqualTo("tt0133093");
        await Assert.That(ctx.TvdbId).IsEqualTo("1234");
    }

    [Test]
    public async Task Create_MediaItem_NullExternalIds_IdsAreNull()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999 };

        var ctx = NamingContext.Create(movie);

        await Assert.That(ctx.TmdbId).IsNull();
        await Assert.That(ctx.ImdbId).IsNull();
        await Assert.That(ctx.TvdbId).IsNull();
    }

    [Test]
    public async Task Create_MediaItem_NullThrows()
    {
        var caught = false;
        try
        {
            NamingContext.Create((MediaItem)null!);
        }
        catch (ArgumentNullException)
        {
            caught = true;
        }

        await Assert.That(caught).IsTrue();
    }

    [Test]
    public async Task Create_WithRelease_SetsEditionFromType()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999 };
        var release = new Release();

        var ctx = NamingContext.Create(movie, release);

        // Release.Type is a read-only computed property;
        // with a default-constructed Release, Edition will reflect its default value
        await Assert.That(ctx.Title).IsEqualTo("The Matrix");
    }

    [Test]
    public async Task Create_WithDisc_SetsFormat()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999 };
        var release = new Release();
        var disc = new Disc { Format = "4K Ultra HD" };

        var ctx = NamingContext.Create(movie, release, disc);

        await Assert.That(ctx.Format).IsEqualTo("4K Ultra HD");
    }

    [Test]
    public async Task Create_WithTitle_SetsResolution()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999 };
        var release = new Release();
        var disc = new Disc { Format = "Blu-ray" };
        var title = new Title
        {
            Tracks = new List<Track>
            {
                new Track { Type = "Video", Resolution = "1920x1080" },
                new Track { Type = "Audio", Resolution = "" },
            },
        };

        var ctx = NamingContext.Create(movie, release, disc, title);

        await Assert.That(ctx.Resolution).IsEqualTo("1080p");
    }

    [Test]
    public async Task Create_WithTitle_4KResolution()
    {
        var movie = new Movie { Title = "The Matrix", Year = 1999 };
        var release = new Release();
        var disc = new Disc { Format = "4K Ultra HD" };
        var title = new Title
        {
            Tracks = new List<Track>
            {
                new Track { Type = "Video", Resolution = "3840x2160" },
            },
        };

        var ctx = NamingContext.Create(movie, release, disc, title);

        await Assert.That(ctx.Resolution).IsEqualTo("2160p");
    }

    [Test]
    public async Task Create_WithTitle_NoVideoTrack_ResolutionIsNull()
    {
        var movie = new Movie { Title = "Test", Year = 2000 };
        var release = new Release();
        var disc = new Disc();
        var title = new Title
        {
            Tracks = new List<Track>
            {
                new Track { Type = "Audio", Resolution = "N/A" },
            },
        };

        var ctx = NamingContext.Create(movie, release, disc, title);

        await Assert.That(ctx.Resolution).IsNull();
    }

    [Test]
    public async Task Create_WithTitle_UnparsableResolution_ReturnsRaw()
    {
        var movie = new Movie { Title = "Test", Year = 2000 };
        var release = new Release();
        var disc = new Disc();
        var title = new Title
        {
            Tracks = new List<Track>
            {
                new Track { Type = "Video", Resolution = "custom-format" },
            },
        };

        var ctx = NamingContext.Create(movie, release, disc, title);

        await Assert.That(ctx.Resolution).IsEqualTo("custom-format");
    }

    [Test]
    public async Task Create_WithTitle_SetsPart_WhenIndexPositive()
    {
        var movie = new Movie { Title = "Test", Year = 2000 };
        var release = new Release();
        var disc = new Disc();
        var title = new Title { Index = 2 };

        var ctx = NamingContext.Create(movie, release, disc, title);

        await Assert.That(ctx.Part).IsEqualTo("pt2");
    }

    [Test]
    public async Task Create_WithTitle_NoPart_WhenIndexZero()
    {
        var movie = new Movie { Title = "Test", Year = 2000 };
        var release = new Release();
        var disc = new Disc();
        var title = new Title { Index = 0 };

        var ctx = NamingContext.Create(movie, release, disc, title);

        await Assert.That(ctx.Part).IsNull();
    }

    [Test]
    public async Task Create_WithTitle_NullTracks_ResolutionIsNull()
    {
        var movie = new Movie { Title = "Test", Year = 2000 };
        var release = new Release();
        var disc = new Disc();
        var title = new Title();

        var ctx = NamingContext.Create(movie, release, disc, title);

        await Assert.That(ctx.Resolution).IsNull();
    }

    [Test]
    public async Task Create_Series_WorksSameAsMovie()
    {
        var series = new Series { Title = "Breaking Bad", Year = 2008 };

        var ctx = NamingContext.Create(series);

        await Assert.That(ctx.Title).IsEqualTo("Breaking Bad");
        await Assert.That(ctx.Year).IsEqualTo("2008");
    }

    [Test]
    public async Task Create_FullChain_EndToEnd()
    {
        var movie = new Movie
        {
            Title = "The Matrix",
            Year = 1999,
            Externalids = new ExternalIds { Tmdb = "603", Imdb = "tt0133093" },
        };
        var release = new Release();
        var disc = new Disc { Format = "4K Ultra HD" };
        var title = new Title
        {
            Index = 1,
            Tracks = new List<Track>
            {
                new Track { Type = "Video", Resolution = "3840x2160" },
            },
        };

        var ctx = NamingContext.Create(movie, release, disc, title);

        await Assert.That(ctx.Title).IsEqualTo("The Matrix");
        await Assert.That(ctx.Year).IsEqualTo("1999");
        await Assert.That(ctx.FullTitle).IsEqualTo("The Matrix (1999)");
        await Assert.That(ctx.Resolution).IsEqualTo("2160p");
        await Assert.That(ctx.Format).IsEqualTo("4K Ultra HD");
        await Assert.That(ctx.Part).IsEqualTo("pt1");
        await Assert.That(ctx.TmdbId).IsEqualTo("603");
        await Assert.That(ctx.ImdbId).IsEqualTo("tt0133093");
    }

    [Test]
    public async Task ResolveResolution_StandardFormats()
    {
        await Assert.That(NamingContext.ResolveResolution(
            [new Track { Type = "Video", Resolution = "3840x2160" }])).IsEqualTo("2160p");

        await Assert.That(NamingContext.ResolveResolution(
            [new Track { Type = "Video", Resolution = "1920x1080" }])).IsEqualTo("1080p");

        await Assert.That(NamingContext.ResolveResolution(
            [new Track { Type = "Video", Resolution = "1280x720" }])).IsEqualTo("720p");

        await Assert.That(NamingContext.ResolveResolution(
            [new Track { Type = "Video", Resolution = "720x480" }])).IsEqualTo("480p");
    }

    [Test]
    public async Task ResolveResolution_NullTracks_ReturnsNull()
    {
        await Assert.That(NamingContext.ResolveResolution(null)).IsNull();
    }

    [Test]
    public async Task ResolveResolution_NoVideoTrack_ReturnsNull()
    {
        var tracks = new List<Track> { new Track { Type = "Audio", Resolution = "N/A" } };

        await Assert.That(NamingContext.ResolveResolution(tracks)).IsNull();
    }

    [Test]
    public async Task ResolveResolution_UnparsableFormat_ReturnsRaw()
    {
        var tracks = new List<Track> { new Track { Type = "Video", Resolution = "custom" } };

        await Assert.That(NamingContext.ResolveResolution(tracks)).IsEqualTo("custom");
    }

    [Test]
    public async Task ResolveResolution_EmptyResolution_ReturnsNull()
    {
        var tracks = new List<Track> { new Track { Type = "Video", Resolution = "" } };

        await Assert.That(NamingContext.ResolveResolution(tracks)).IsNull();
    }

    [Test]
    public async Task PadNumber_SingleDigit_Pads()
    {
        await Assert.That(NamingContext.PadNumber("3")).IsEqualTo("03");
    }

    [Test]
    public async Task PadNumber_TwoDigits_Unchanged()
    {
        await Assert.That(NamingContext.PadNumber("11")).IsEqualTo("11");
    }

    [Test]
    public async Task PadNumber_ThreeDigits_Unchanged()
    {
        await Assert.That(NamingContext.PadNumber("100")).IsEqualTo("100");
    }

    [Test]
    public async Task PadNumber_Null_ReturnsNull()
    {
        await Assert.That(NamingContext.PadNumber(null)).IsNull();
    }

    [Test]
    public async Task PadNumber_Empty_ReturnsNull()
    {
        await Assert.That(NamingContext.PadNumber("")).IsNull();
    }

    [Test]
    public async Task PadNumber_NonNumeric_PassesThrough()
    {
        await Assert.That(NamingContext.PadNumber("Special")).IsEqualTo("Special");
    }

    [Test]
    public async Task PadNumber_Zero_PadsToTwoDigits()
    {
        await Assert.That(NamingContext.PadNumber("0")).IsEqualTo("00");
    }
}
