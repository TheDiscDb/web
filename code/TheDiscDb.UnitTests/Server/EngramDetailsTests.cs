using System.ComponentModel.DataAnnotations;
using TheDiscDb.Components.Pages.Contribute;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.Server;

public class EngramDetailsTests
{
    [Test]
    public async Task InferMediaType_NoDetectedSeason_ReturnsMovie()
    {
        var disc = new EngramDisc { ContentHash = "ABC", DetectedSeason = null };
        await Assert.That(EngramDetails.InferMediaType(disc)).IsEqualTo("movie");
    }

    [Test]
    public async Task InferMediaType_HasDetectedSeason_ReturnsSeries()
    {
        var disc = new EngramDisc { ContentHash = "ABC", DetectedSeason = 1 };
        await Assert.That(EngramDetails.InferMediaType(disc)).IsEqualTo("series");
    }

    [Test]
    [Arguments("blu-ray", "Blu-ray")]
    [Arguments("BLU-RAY", "Blu-ray")]
    [Arguments("dvd", "DVD")]
    [Arguments("4k", "4K")]
    [Arguments("", "Blu-ray")]
    [Arguments("unknown", "Blu-ray")]
    public async Task MapContentTypeToFormat_MapsKnownAndDefaults(string input, string expected)
    {
        await Assert.That(EngramDetails.MapContentTypeToFormat(input)).IsEqualTo(expected);
    }

    [Test]
    public async Task CreateTitleSlug_NameAndYear_AppendsYear()
    {
        var slug = EngramDetails.CreateTitleSlug("The Matrix", "1999");
        await Assert.That(slug).IsEqualTo("the-matrix-1999");
    }

    [Test]
    public async Task CreateTitleSlug_NameOnly_NoYearSuffix()
    {
        var slug = EngramDetails.CreateTitleSlug("The Matrix", null);
        await Assert.That(slug).IsEqualTo("the-matrix");
    }

    [Test]
    public async Task CreateTitleSlug_EmptyName_ReturnsEmpty()
    {
        await Assert.That(EngramDetails.CreateTitleSlug(null, "1999")).IsEqualTo(string.Empty);
        await Assert.That(EngramDetails.CreateTitleSlug("", "1999")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task FormatDuration_HoursAndMinutes()
    {
        await Assert.That(EngramDetails.FormatDuration(3661)).IsEqualTo("1h 1m 1s");
    }

    [Test]
    public async Task FormatDuration_MinutesOnly()
    {
        await Assert.That(EngramDetails.FormatDuration(125)).IsEqualTo("2m 5s");
    }

    [Test]
    public async Task FormatDuration_Null_ReturnsEmpty()
    {
        await Assert.That(EngramDetails.FormatDuration(null)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task FormatSize_Gigabytes()
    {
        var size = EngramDetails.FormatSize(2L * 1_073_741_824);
        await Assert.That(size).IsEqualTo("2.0 GB");
    }

    [Test]
    public async Task FormatSize_Megabytes()
    {
        var size = EngramDetails.FormatSize(5L * 1_048_576);
        await Assert.That(size).IsEqualTo("5.0 MB");
    }

    [Test]
    public async Task CreateFromEngramRequest_Defaults_AreFriendly()
    {
        var request = new CreateFromEngramRequest();
        await Assert.That(request.MediaType).IsEqualTo("movie");
        await Assert.That(request.Locale).IsEqualTo("en-US");
        await Assert.That(request.RegionCode).IsEqualTo("1");
        await Assert.That(request.ReleaseDate).IsNull();
    }

    [Test]
    public async Task CreateFromEngramRequest_MissingRequiredFields_FailValidation()
    {
        var request = new CreateFromEngramRequest
        {
            MediaType = "movie",
            // Asin/Upc deliberately null
            Locale = "en-US",
            RegionCode = "1"
        };

        var ctx = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, ctx, results, validateAllProperties: true);

        await Assert.That(isValid).IsFalse();
        await Assert.That(results.Any(r => r.ErrorMessage!.Contains("ASIN", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(results.Any(r => r.ErrorMessage!.Contains("UPC", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task CreateFromEngramRequest_AllRequiredFilled_PassesValidation()
    {
        var request = new CreateFromEngramRequest
        {
            MediaType = "movie",
            Asin = "B01N5IB20Q",
            Upc = "883929547654",
            ReleaseDate = DateTimeOffset.UtcNow,
            Locale = "en-US",
            RegionCode = "1"
        };

        var ctx = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, ctx, results, validateAllProperties: true);

        await Assert.That(isValid).IsTrue();
    }

    [Test]
    public async Task MatchEngramHint_BySegmentMap_Matches()
    {
        var hints = new List<EngramTitle>
        {
            new() { TitleIndex = 0, SegmentMap = "1+2+3", TitleType = "movie" },
            new() { TitleIndex = 1, SegmentMap = "4+5", TitleType = "extra" },
        };
        var parsed = new MakeMkv.Title { Index = 99, SegmentMap = "4+5" };

        var hit = EngramDetails.MatchEngramHint(parsed, hints);

        await Assert.That(hit).IsNotNull();
        await Assert.That(hit!.TitleType).IsEqualTo("extra");
    }

    [Test]
    public async Task MatchEngramHint_FallsBackToTitleIndex()
    {
        var hints = new List<EngramTitle>
        {
            new() { TitleIndex = 0, SegmentMap = null, TitleType = "movie" },
            new() { TitleIndex = 7, SegmentMap = null, TitleType = "extra" },
        };
        var parsed = new MakeMkv.Title { Index = 7, SegmentMap = null };

        var hit = EngramDetails.MatchEngramHint(parsed, hints);

        await Assert.That(hit).IsNotNull();
        await Assert.That(hit!.TitleType).IsEqualTo("extra");
    }

    [Test]
    public async Task MatchEngramHint_NoMatch_ReturnsNull()
    {
        var hints = new List<EngramTitle>
        {
            new() { TitleIndex = 0, SegmentMap = "1+2+3", TitleType = "movie" },
        };
        var parsed = new MakeMkv.Title { Index = 99, SegmentMap = "9+9+9" };

        var hit = EngramDetails.MatchEngramHint(parsed, hints);

        await Assert.That(hit).IsNull();
    }

    [Test]
    public async Task MatchEngramHint_EmptyHints_ReturnsNull()
    {
        var parsed = new MakeMkv.Title { Index = 0, SegmentMap = "1" };
        var hit = EngramDetails.MatchEngramHint(parsed, new List<EngramTitle>());
        await Assert.That(hit).IsNull();
    }

    [Test]
    public async Task AddItemsFromParsedTitles_OnlyAddsItemsWithMatchingHint()
    {
        var disc = new UserContributionDisc { ContentHash = "ABC" };
        var hints = new List<EngramTitle>
        {
            new() { TitleIndex = 0, SegmentMap = "1+2+3", TitleType = "movie", SourceFilename = "movie.mkv" },
            new() { TitleIndex = 1, SegmentMap = "4+5", TitleType = "extra", SourceFilename = "extra.mkv" },
        };
        var parsed = new List<MakeMkv.Title>
        {
            new() { Index = 0, SegmentMap = "1+2+3", DisplaySize = "30.7 GB", ChapterCount = 24 },
            new() { Index = 1, SegmentMap = "4+5", DisplaySize = "1.2 GB", ChapterCount = 1 },
            new() { Index = 2, SegmentMap = "9+9", DisplaySize = "100 MB", ChapterCount = 1 }, // not in engram hints
            new() { Index = 3, SegmentMap = "10", DisplaySize = "5 MB", ChapterCount = 1 },    // not in engram hints
        };

        EngramDetails.AddItemsFromParsedTitles(disc, parsed, hints);

        await Assert.That(disc.Items.Count).IsEqualTo(2);
        await Assert.That(disc.Items.Any(i => i.SegmentMap == "9+9")).IsFalse();
        await Assert.That(disc.Items.Any(i => i.SegmentMap == "10")).IsFalse();
        await Assert.That(disc.Items.Any(i => i.SegmentMap == "1+2+3" && i.Type == "movie")).IsTrue();
        await Assert.That(disc.Items.Any(i => i.SegmentMap == "4+5" && i.Type == "extra")).IsTrue();
    }

    [Test]
    public async Task AddItemsFromParsedTitles_NoEngramHints_AddsNothing()
    {
        var disc = new UserContributionDisc { ContentHash = "ABC" };
        var parsed = new List<MakeMkv.Title>
        {
            new() { Index = 0, SegmentMap = "1+2+3", DisplaySize = "30.7 GB", ChapterCount = 24 },
        };

        EngramDetails.AddItemsFromParsedTitles(disc, parsed, new List<EngramTitle>());

        await Assert.That(disc.Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AddItemsFromParsedTitles_UsesDisplaySizeFromParsedNotFormatSize()
    {
        // Regression: the original code used FormatSize() which produced "30.0 GB" or
        // similar — this never matched MakeMKV's DisplaySize used by IdentifyDiscItems
        // for matching items to scan-log titles.
        var disc = new UserContributionDisc { ContentHash = "ABC" };
        var hints = new List<EngramTitle>
        {
            new() { TitleIndex = 0, SegmentMap = "1+2+3", SizeBytes = 32_212_254_720L },
        };
        var parsed = new List<MakeMkv.Title>
        {
            new() { Index = 0, SegmentMap = "1+2+3", DisplaySize = "30.7 GB", ChapterCount = 24 },
        };

        EngramDetails.AddItemsFromParsedTitles(disc, parsed, hints);

        await Assert.That(disc.Items.Count).IsEqualTo(1);
        await Assert.That(disc.Items.First().Size).IsEqualTo("30.7 GB");
    }
}
