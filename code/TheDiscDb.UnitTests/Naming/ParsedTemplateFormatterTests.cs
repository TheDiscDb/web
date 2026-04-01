using TheDiscDb.Naming;

namespace TheDiscDb.UnitTests.Naming;

public class ParsedTemplateFormatterTests
{
    [Test]
    public async Task Format_AllTokensPopulated_ProducesCorrectOutput()
    {
        var result = NamingTemplate.Parse("{title} ({year}) [{resolution}].mkv");
        var context = new NamingContext
        {
            Title = "The Matrix",
            Year = "1999",
            Resolution = "2160p",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("The Matrix (1999) [2160p].mkv");
    }

    [Test]
    public async Task Format_MissingTokenAtMiddle_TrimsAdjacentSpace()
    {
        var result = NamingTemplate.Parse("{title} {edition} [{resolution}].mkv");
        var context = new NamingContext
        {
            Title = "The Matrix",
            Resolution = "1080p",
        };

        var output = result.Template!.Format(context);

        // Edition is missing → preceding space trimmed
        await Assert.That(output).IsEqualTo("The Matrix [{resolution}].mkv".Replace("{resolution}", "1080p"));
        await Assert.That(output).IsEqualTo("The Matrix [1080p].mkv");
    }

    [Test]
    public async Task Format_MissingTokenAtStart_TrimsFollowingSpace()
    {
        var result = NamingTemplate.Parse("{edition} {title}.mkv");
        var context = new NamingContext
        {
            Title = "The Matrix",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("The Matrix.mkv");
    }

    [Test]
    public async Task Format_MissingTokenAtEnd_TrimsPrecedingSpace()
    {
        var result = NamingTemplate.Parse("{title} {edition}");
        var context = new NamingContext
        {
            Title = "The Matrix",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("The Matrix");
    }

    [Test]
    public async Task Format_MultipleMissingTokens_EachTrimsOneSpace()
    {
        var result = NamingTemplate.Parse("{title} {edition} {part} {resolution}");
        var context = new NamingContext
        {
            Title = "The Matrix",
            Resolution = "1080p",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("The Matrix 1080p");
    }

    [Test]
    public async Task Format_AllTokensMissing_ReturnsLiteralsOnly()
    {
        var result = NamingTemplate.Parse("Movie - {title} ({year}).mkv");
        var context = new NamingContext();

        var output = result.Template!.Format(context);

        // "Movie - " trims trailing space for missing title → "Movie -"
        // " (" doesn't end in space, so no trim for missing year
        await Assert.That(output).IsEqualTo("Movie - ().mkv");
    }

    [Test]
    public async Task Format_MissingTokenBetweenDelimiters_DelimitersRemain()
    {
        var result = NamingTemplate.Parse("{title} [{edition}].mkv");
        var context = new NamingContext
        {
            Title = "The Matrix",
        };

        var output = result.Template!.Format(context);

        // Conservative: brackets stay, only whitespace trimmed
        await Assert.That(output).IsEqualTo("The Matrix [].mkv");
    }

    [Test]
    public async Task Format_WhitespaceOnlyValue_TreatedAsMissing()
    {
        var result = NamingTemplate.Parse("{title} {edition}.mkv");
        var context = new NamingContext
        {
            Title = "The Matrix",
            Edition = "   ",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("The Matrix.mkv");
    }

    [Test]
    public async Task Format_EmptyStringValue_TreatedAsMissing()
    {
        var result = NamingTemplate.Parse("{title} {edition}.mkv");
        var context = new NamingContext
        {
            Title = "The Matrix",
            Edition = "",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("The Matrix.mkv");
    }

    [Test]
    public async Task Format_ColonInValue_SanitizedToHyphen()
    {
        var result = NamingTemplate.Parse("{title} {edition}.mkv");
        var context = new NamingContext
        {
            Title = "The Matrix",
            Edition = "Director's Cut: Extended",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("The Matrix Director's Cut - Extended.mkv");
    }

    [Test]
    public async Task Format_FullRealWorldExample_Movie()
    {
        var result = NamingTemplate.Parse("{title} ({year}) [{resolution}].mkv");
        var context = new NamingContext
        {
            Title = "The Matrix",
            Year = "1999",
            Resolution = "2160p",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("The Matrix (1999) [2160p].mkv");
    }

    [Test]
    public async Task Format_FullRealWorldExample_Episode()
    {
        var result = NamingTemplate.Parse("{title} - S{seasonNumber}E{episodeNumber} - {episodeName}.mkv");
        var context = new NamingContext
        {
            Title = "Breaking Bad",
            SeasonNumber = "05",
            EpisodeNumber = "16",
            EpisodeName = "Felina",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("Breaking Bad - S05E16 - Felina.mkv");
    }

    [Test]
    public async Task Format_CaseInsensitiveTokens_ResolveCorrectly()
    {
        var result = NamingTemplate.Parse("{TITLE} ({Year})");
        var context = new NamingContext
        {
            Title = "Inception",
            Year = "2010",
        };

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("Inception (2010)");
    }

    [Test]
    public async Task Format_EscapedBracesInTemplate_ProducesLiteralBraces()
    {
        var result = NamingTemplate.Parse("{{{title}}}");
        var context = new NamingContext
        {
            Title = "Test",
        };

        var output = result.Template!.Format(context);

        // {{ → {, then Token(title), then }} → }
        // But { and } are not illegal filename chars in our sanitizer, so they stay
        await Assert.That(output).IsEqualTo("{Test}");
    }

    [Test]
    public async Task Format_MissingTokenNoAdjacentWhitespace_JustOmitted()
    {
        var result = NamingTemplate.Parse("{title}({edition})");
        var context = new NamingContext
        {
            Title = "The Matrix",
        };

        var output = result.Template!.Format(context);

        // No space to trim, token just becomes empty
        await Assert.That(output).IsEqualTo("The Matrix()");
    }

    [Test]
    public async Task Format_LiteralOnlyTemplate_ReturnsLiteral()
    {
        var result = NamingTemplate.Parse("movie.mkv");
        var context = new NamingContext();

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("movie.mkv");
    }

    [Test]
    public async Task Format_EmptyTemplate_ReturnsEmptyString()
    {
        var result = NamingTemplate.Parse("");
        var context = new NamingContext();

        var output = result.Template!.Format(context);

        await Assert.That(output).IsEqualTo("");
    }
}
