namespace TheDiscDb.UnitTests.Client;

using TheDiscDb.Chapters;

public class ChapterParserTests
{
    private const string SampleTextFormat = """
        CHAPTER01=00:00:00.000
        CHAPTER01NAME=Dead in the Water
        CHAPTER02=00:08:56.327
        CHAPTER02NAME=The Lost Command
        CHAPTER03=00:13:12.917
        CHAPTER03NAME=A Special Op
        """;

    private const string SampleXmlFormat = """
        <chapterInfo xml:lang="eng" version="2" extractor="ChapterText" client="ChapterGrabber 5.4" confirmations="0" xmlns="http://jvance.com/2008/ChapterGrabber">
          <title>U-571.chapters(1)</title>
          <ref>
            <chapterSetId>243187</chapterSetId>
          </ref>
          <source>
            <type>Unknown</type>
          </source>
          <chapters>
            <chapter time="00:00:00" name="Dead in the Water" />
            <chapter time="00:08:56.3270000" name="The Lost Command" />
            <chapter time="00:13:12.9170000" name="A Special Op" />
          </chapters>
        </chapterInfo>
        """;

    // --- ChapterParser (orchestrator) ---

    [Test]
    public async Task TryParseChapters_WithTextFormat_ReturnsTrue()
    {
        var result = ChapterParser.TryParseChapters(SampleTextFormat, out var names);

        await Assert.That(result).IsTrue();
        await Assert.That(names).Count().IsEqualTo(3);
        await Assert.That(names[0]).IsEqualTo("Dead in the Water");
        await Assert.That(names[1]).IsEqualTo("The Lost Command");
        await Assert.That(names[2]).IsEqualTo("A Special Op");
    }

    [Test]
    public async Task TryParseChapters_WithXmlFormat_ReturnsTrue()
    {
        var result = ChapterParser.TryParseChapters(SampleXmlFormat, out var names);

        await Assert.That(result).IsTrue();
        await Assert.That(names).Count().IsEqualTo(3);
        await Assert.That(names[0]).IsEqualTo("Dead in the Water");
    }

    [Test]
    public async Task TryParseChapters_WithRandomText_ReturnsFalse()
    {
        var result = ChapterParser.TryParseChapters("just some random text", out var names);

        await Assert.That(result).IsFalse();
        await Assert.That(names).IsEmpty();
    }

    [Test]
    public async Task TryParseChapters_WithEmptyString_ReturnsFalse()
    {
        var result = ChapterParser.TryParseChapters("", out var names);

        await Assert.That(result).IsFalse();
        await Assert.That(names).IsEmpty();
    }

    [Test]
    public async Task TryParseChapters_WithNull_ReturnsFalse()
    {
        var result = ChapterParser.TryParseChapters(null!, out var names);

        await Assert.That(result).IsFalse();
        await Assert.That(names).IsEmpty();
    }

    // --- TextChapterParser ---

    [Test]
    public async Task TextChapterParser_ParsesChapterNames()
    {
        var parser = new TextChapterParser();
        var result = parser.TryParse(SampleTextFormat, out var names);

        await Assert.That(result).IsTrue();
        await Assert.That(names).Count().IsEqualTo(3);
    }

    [Test]
    public async Task TextChapterParser_TrimsWhitespace()
    {
        var parser = new TextChapterParser();
        var input = "CHAPTER01=00:00:00.000\r\nCHAPTER01NAME=  Padded Name  \r\n";

        var result = parser.TryParse(input, out var names);

        await Assert.That(result).IsTrue();
        await Assert.That(names[0]).IsEqualTo("Padded Name");
    }

    [Test]
    public async Task TextChapterParser_WithNoNameLines_ReturnsFalse()
    {
        var parser = new TextChapterParser();
        var input = "CHAPTER01=00:00:00.000\nCHAPTER02=00:05:00.000\n";

        var result = parser.TryParse(input, out var names);

        await Assert.That(result).IsFalse();
        await Assert.That(names).IsEmpty();
    }

    // --- XmlChapterParser ---

    [Test]
    public async Task XmlChapterParser_ParsesChapterNames()
    {
        var parser = new XmlChapterParser();
        var result = parser.TryParse(SampleXmlFormat, out var names);

        await Assert.That(result).IsTrue();
        await Assert.That(names).Count().IsEqualTo(3);
    }

    [Test]
    public async Task XmlChapterParser_WithoutNamespace_ParsesChapterNames()
    {
        var parser = new XmlChapterParser();
        var input = """
            <chapterInfo>
              <chapters>
                <chapter time="00:00:00" name="Chapter One" />
                <chapter time="00:05:00" name="Chapter Two" />
              </chapters>
            </chapterInfo>
            """;

        var result = parser.TryParse(input, out var names);

        await Assert.That(result).IsTrue();
        await Assert.That(names).Count().IsEqualTo(2);
        await Assert.That(names[0]).IsEqualTo("Chapter One");
    }

    [Test]
    public async Task XmlChapterParser_WithInvalidXml_ReturnsFalse()
    {
        var parser = new XmlChapterParser();
        var result = parser.TryParse("not xml at all", out var names);

        await Assert.That(result).IsFalse();
        await Assert.That(names).IsEmpty();
    }

    [Test]
    public async Task XmlChapterParser_WithXmlButNoChapters_ReturnsFalse()
    {
        var parser = new XmlChapterParser();
        var input = "<root><item>stuff</item></root>";

        var result = parser.TryParse(input, out var names);

        await Assert.That(result).IsFalse();
        await Assert.That(names).IsEmpty();
    }
}
