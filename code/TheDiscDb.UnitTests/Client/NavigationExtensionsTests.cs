using TheDiscDb.InputModels;

namespace TheDiscDb.UnitTests.Client;

public class NavigationExtensionsTests
{
    [Test]
    public async Task GetFile_ValidSourceFile_ReturnsFileNameWithoutExtension()
    {
        var result = NavigationExtensions.GetFile("00801.mpls");

        await Assert.That(result).IsEqualTo("00801");
    }

    [Test]
    public async Task GetFile_NullOrEmpty_ReturnsEmpty()
    {
        await Assert.That(NavigationExtensions.GetFile(null)).IsEqualTo(string.Empty);
        await Assert.That(NavigationExtensions.GetFile("")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetFile_NoDot_ReturnsOriginal()
    {
        var result = NavigationExtensions.GetFile("nodot");

        await Assert.That(result).IsEqualTo("nodot");
    }

    [Test]
    public async Task GetExtension_ValidSourceFile_ReturnsExtension()
    {
        var result = NavigationExtensions.GetExtension("00801.mpls");

        await Assert.That(result).IsEqualTo("mpls");
    }

    [Test]
    public async Task GetExtension_NullOrEmpty_ReturnsEmpty()
    {
        await Assert.That(NavigationExtensions.GetExtension(null)).IsEqualTo(string.Empty);
        await Assert.That(NavigationExtensions.GetExtension("")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetExtension_NoDot_ReturnsEmpty()
    {
        var result = NavigationExtensions.GetExtension("nodot");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetExtension_WithParentheses_EncodesAsBrackets()
    {
        var result = NavigationExtensions.GetExtension("file.m2ts(3)");

        await Assert.That(result).IsEqualTo("m2ts[3]");
    }

    [Test]
    public async Task EncodeExtension_ReplacesParentheses()
    {
        var result = NavigationExtensions.EncodeExtension("m2ts(3)");

        await Assert.That(result).IsEqualTo("m2ts[3]");
    }

    [Test]
    public async Task EncodeExtension_NullOrEmpty_ReturnsEmpty()
    {
        await Assert.That(NavigationExtensions.EncodeExtension("")).IsEqualTo(string.Empty);
        await Assert.That(NavigationExtensions.EncodeExtension(null!)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DecodeExtension_ReplacesBrackets()
    {
        var result = NavigationExtensions.DecodeExtension("m2ts[3]");

        await Assert.That(result).IsEqualTo("m2ts(3)");
    }

    [Test]
    public async Task DecodeExtension_NullOrEmpty_ReturnsEmpty()
    {
        await Assert.That(NavigationExtensions.DecodeExtension("")).IsEqualTo(string.Empty);
        await Assert.That(NavigationExtensions.DecodeExtension(null!)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task EncodeAndDecode_AreInverses()
    {
        var original = "m2ts(3)";
        var encoded = NavigationExtensions.EncodeExtension(original);
        var decoded = NavigationExtensions.DecodeExtension(encoded);

        await Assert.That(decoded).IsEqualTo(original);
    }

    [Test]
    public async Task GetSourceFile_WithFileAndExtension_CombinesThem()
    {
        var result = NavigationExtensions.GetSourceFile("00801", "mpls");

        await Assert.That(result).IsEqualTo("00801.mpls");
    }

    [Test]
    public async Task GetSourceFile_WithFileAndDashExtension_ReturnsFileOnly()
    {
        var result = NavigationExtensions.GetSourceFile("00801", "-");

        await Assert.That(result).IsEqualTo("00801");
    }

    [Test]
    public async Task GetSourceFile_EmptyFile_ReturnsEmpty()
    {
        var result = NavigationExtensions.GetSourceFile("", "mpls");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetSourceFile_NullExtension_ReturnsFileOnly()
    {
        var result = NavigationExtensions.GetSourceFile("00801", null);

        await Assert.That(result).IsEqualTo("00801");
    }

    [Test]
    public async Task GetSourceFile_WithEncodedExtension_DecodesParentheses()
    {
        var result = NavigationExtensions.GetSourceFile("file", "m2ts[3]");

        await Assert.That(result).IsEqualTo("file.m2ts(3)");
    }

    [Test]
    public async Task ItemDetailUrl_NullItem_ReturnsEmpty()
    {
        IDisplayItem? item = null;
        var result = item!.ItemDetailUrl();

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ItemDetailUrl_ValidItem_ReturnsCorrectUrl()
    {
        var item = new TestDisplayItem { Type = "Movie", Slug = "the-matrix" };
        var result = item.ItemDetailUrl();

        await Assert.That(result).IsEqualTo("/movie/the-matrix");
    }

    [Test]
    public async Task ItemTypeForUrl_ReturnsLowercase()
    {
        var item = new TestDisplayItem { Type = "MOVIE" };
        var result = item.ItemTypeForUrl();

        await Assert.That(result).IsEqualTo("movie");
    }

    [Test]
    public async Task ItemTypeForUrl_NullType_ReturnsEmpty()
    {
        var item = new TestDisplayItem { Type = null };
        var result = item.ItemTypeForUrl();

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DiscFeatureDescription_MainMovie_Singular()
    {
        var feature = new DiscFeature { Type = "MainMovie", Count = 1 };

        await Assert.That(feature.Description).IsEqualTo("1 movie");
    }

    [Test]
    public async Task DiscFeatureDescription_MainMovie_Plural()
    {
        var feature = new DiscFeature { Type = "MainMovie", Count = 3 };

        await Assert.That(feature.Description).IsEqualTo("3 movies");
    }

    [Test]
    public async Task DiscFeatureDescription_Extra_Singular()
    {
        var feature = new DiscFeature { Type = "Extra", Count = 1 };

        await Assert.That(feature.Description).IsEqualTo("1 extra");
    }

    [Test]
    public async Task DiscFeatureDescription_Extra_Plural()
    {
        var feature = new DiscFeature { Type = "Extra", Count = 5 };

        await Assert.That(feature.Description).IsEqualTo("5 extras");
    }

    [Test]
    public async Task DiscFeatureDescription_Trailer_Singular()
    {
        var feature = new DiscFeature { Type = "Trailer", Count = 1 };

        await Assert.That(feature.Description).IsEqualTo("1 trailer");
    }

    [Test]
    public async Task DiscFeatureDescription_DeletedScene_Singular()
    {
        var feature = new DiscFeature { Type = "DeletedScene", Count = 1 };

        await Assert.That(feature.Description).IsEqualTo("1 deleted scene");
    }

    [Test]
    public async Task DiscFeatureDescription_DeletedScene_Plural()
    {
        var feature = new DiscFeature { Type = "DeletedScene", Count = 4 };

        await Assert.That(feature.Description).IsEqualTo("4 deleted scenes");
    }

    [Test]
    public async Task DiscFeatureDescription_Episode_Singular()
    {
        var feature = new DiscFeature { Type = "Episode", Count = 1 };

        await Assert.That(feature.Description).IsEqualTo("1 episode");
    }

    [Test]
    public async Task DiscFeatureDescription_Episode_Plural()
    {
        var feature = new DiscFeature { Type = "Episode", Count = 10 };

        await Assert.That(feature.Description).IsEqualTo("10 episodes");
    }

    [Test]
    public async Task DiscFeatureDescription_NullType_ReturnsEmpty()
    {
        var feature = new DiscFeature { Type = null, Count = 1 };

        await Assert.That(feature.Description).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DiscFeatureDescription_UnknownType_ReturnsEmpty()
    {
        var feature = new DiscFeature { Type = "UnknownType", Count = 1 };

        await Assert.That(feature.Description).IsEqualTo("");
    }

    [Test]
    public async Task GetDesciption_VideoTrack_ReturnsFormattedString()
    {
        var track = new Track { Type = "Video", Name = "H.265", AspectRatio = "16:9", Resolution = "3840x2160" };
        var result = track.GetDesciption();

        await Assert.That(result).IsEqualTo("H.265 16:9 (3840x2160)");
    }

    [Test]
    public async Task GetDesciption_AudioTrack_ReturnsFormattedString()
    {
        var track = new Track { Type = "Audio", Name = "Dolby Atmos", AudioType = "TrueHD", Language = "English" };
        var result = track.GetDesciption();

        await Assert.That(result).IsEqualTo("Dolby Atmos TrueHD (English)");
    }

    [Test]
    public async Task GetDesciption_SubtitlesTrack_ReturnsFormattedString()
    {
        var track = new Track { Type = "Subtitles", Name = "PGS", Language = "English" };
        var result = track.GetDesciption();

        await Assert.That(result).IsEqualTo("PGS (English)");
    }

    [Test]
    public async Task GetDesciption_NullType_ReturnsEmpty()
    {
        var track = new Track { Type = null };
        var result = track.GetDesciption();

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    private class TestDisplayItem : IDisplayItem
    {
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? ImageUrl { get; set; }
        public string? Type { get; set; }
    }
}
