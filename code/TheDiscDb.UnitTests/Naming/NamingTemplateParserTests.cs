using TheDiscDb.Naming;

namespace TheDiscDb.UnitTests.Naming;

public class NamingTemplateParserTests
{
    [Test]
    public async Task Parse_SingleToken_ReturnsSegments()
    {
        var result = NamingTemplate.Parse("{title}");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Template!.Segments).Count().IsEqualTo(1);
        await Assert.That(result.Template.Segments[0]).IsTypeOf<TokenSegment>();
    }

    [Test]
    public async Task Parse_LiteralOnly_ReturnsLiteralSegment()
    {
        var result = NamingTemplate.Parse("hello world.mkv");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Template!.Segments).Count().IsEqualTo(1);
        await Assert.That(result.Template.Segments[0]).IsTypeOf<LiteralSegment>();
    }

    [Test]
    public async Task Parse_MultipleTokens_ReturnsAllSegments()
    {
        var result = NamingTemplate.Parse("{title} ({year}) [{resolution}].mkv");

        await Assert.That(result.IsSuccess).IsTrue();
        // Segments: Token(title), Literal(" ("), Token(year), Literal(") ["), Token(resolution), Literal("].mkv")
        await Assert.That(result.Template!.Segments).Count().IsEqualTo(6);
    }

    [Test]
    public async Task Parse_AdjacentTokens_Succeeds()
    {
        var result = NamingTemplate.Parse("{title}{year}");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Template!.Segments).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Parse_UnknownToken_ReturnsError()
    {
        var result = NamingTemplate.Parse("{foo}");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(1);
        await Assert.That(result.Errors![0].Message).Contains("Unknown token 'foo'");
        await Assert.That(result.Errors![0].Position).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_UnclosedBrace_ReturnsError()
    {
        var result = NamingTemplate.Parse("{title");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(1);
        await Assert.That(result.Errors![0].Message).Contains("Unclosed token");
    }

    [Test]
    public async Task Parse_UnmatchedClosingBrace_ReturnsError()
    {
        var result = NamingTemplate.Parse("title}");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(1);
        await Assert.That(result.Errors![0].Message).Contains("Unexpected '}'");
    }

    [Test]
    public async Task Parse_EmptyTokenName_ReturnsError()
    {
        var result = NamingTemplate.Parse("{}");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(1);
        await Assert.That(result.Errors![0].Message).Contains("Empty token name");
    }

    [Test]
    public async Task Parse_EscapedBraces_ProducesLiteralBraces()
    {
        var result = NamingTemplate.Parse("{{hello}}");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Template!.Segments).Count().IsEqualTo(1);

        var literal = (LiteralSegment)result.Template.Segments[0];
        await Assert.That(literal.Text).IsEqualTo("{hello}");
    }

    [Test]
    public async Task Parse_CaseInsensitive_AcceptsUpperCase()
    {
        var result = NamingTemplate.Parse("{Title}");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Parse_CaseInsensitive_AcceptsMixedCase()
    {
        var result = NamingTemplate.Parse("{FullTitle}");

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Parse_EmptyTemplate_ReturnsEmptySegments()
    {
        var result = NamingTemplate.Parse("");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Template!.Segments).Count().IsEqualTo(0);
    }

    [Test]
    public async Task Parse_MultipleErrors_ReportsAll()
    {
        var result = NamingTemplate.Parse("{foo} {bar}");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Parse_AllKnownTokens_Succeeds()
    {
        var template = "{title}{year}{fullTitle}{resolution}{format}{tmdbId}{imdbId}{tvdbId}{edition}{part}{extraType}{seasonNumber}{episodeNumber}{episodeName}{airDate}";
        var result = NamingTemplate.Parse(template);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Template!.Segments).Count().IsEqualTo(15);
    }

    [Test]
    public async Task Parse_NullTemplate_ReturnsError()
    {
        var result = NamingTemplate.Parse(null!);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Parse_TokenPosition_IsRecorded()
    {
        var result = NamingTemplate.Parse("abc{title}");

        await Assert.That(result.IsSuccess).IsTrue();
        var token = (TokenSegment)result.Template!.Segments[1];
        await Assert.That(token.Position).IsEqualTo(3);
    }

    [Test]
    public async Task Parse_EscapedBracesAdjacentToToken_Succeeds()
    {
        var result = NamingTemplate.Parse("{{{title}}}");

        await Assert.That(result.IsSuccess).IsTrue();
        // Segments: Literal("{"), Token(title), Literal("}")
        await Assert.That(result.Template!.Segments).Count().IsEqualTo(3);

        var first = (LiteralSegment)result.Template.Segments[0];
        await Assert.That(first.Text).IsEqualTo("{");

        var last = (LiteralSegment)result.Template.Segments[2];
        await Assert.That(last.Text).IsEqualTo("}");
    }

    [Test]
    public async Task Parse_WhitespaceOnlyTokenName_ReturnsError()
    {
        var result = NamingTemplate.Parse("{  }");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(1);
        await Assert.That(result.Errors![0].Message).Contains("Empty token name");
    }

    [Test]
    public async Task Parse_UnknownToken_ErrorPositionAndLength_AtOffset()
    {
        var result = NamingTemplate.Parse("abc{foo}xyz");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors![0].Position).IsEqualTo(3);
        await Assert.That(result.Errors![0].Length).IsEqualTo(5);
    }

    [Test]
    public async Task Parse_UnclosedBrace_ErrorLength_SpansToEnd()
    {
        var result = NamingTemplate.Parse("abc{title");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors![0].Position).IsEqualTo(3);
        await Assert.That(result.Errors![0].Length).IsEqualTo(6);
    }

    [Test]
    public async Task Parse_UnmatchedClosingBrace_ErrorPositionAndLength()
    {
        var result = NamingTemplate.Parse("abc}def");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors![0].Position).IsEqualTo(3);
        await Assert.That(result.Errors![0].Length).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_EmptyToken_ErrorLength_IncludesBraces()
    {
        var result = NamingTemplate.Parse("ab{}cd");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors![0].Position).IsEqualTo(2);
        await Assert.That(result.Errors![0].Length).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_MixedValidAndInvalidTokens_ReportsErrorAtCorrectOffset()
    {
        var result = NamingTemplate.Parse("{title} {foo} {year}");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(1);
        await Assert.That(result.Errors![0].Position).IsEqualTo(8);
        await Assert.That(result.Errors![0].Message).Contains("Unknown token 'foo'");
    }

    [Test]
    public async Task Parse_MultipleDifferentErrorTypes_ReportsAll()
    {
        // {unknown} is unknown token, {} is empty token, trailing } is unmatched
        var result = NamingTemplate.Parse("{unknown} {} x}");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(3);
        await Assert.That(result.Errors![0].Message).Contains("Unknown token");
        await Assert.That(result.Errors![1].Message).Contains("Empty token name");
        await Assert.That(result.Errors![2].Message).Contains("Unexpected '}'");
    }

    [Test]
    public async Task Parse_NestedBraces_ReportsUnknownToken()
    {
        // Parser finds { at 0, searches for }, finds it at position 11 (end of "year")
        // Token name extracted: "title{year" — unknown
        // Then trailing } at 12 is an unmatched closing brace
        var result = NamingTemplate.Parse("{title{year}}");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors!).Count().IsEqualTo(2);
    }
}
