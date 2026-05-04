namespace TheDiscDb.UnitTests.Client;

using TheDiscDb.Chapters;
using TheDiscDb.InputModels;

public class ChapterFormatterTests
{
    private static readonly List<Chapter> SampleChapters =
    [
        new Chapter { Index = 1, Title = "Dead in the Water" },
        new Chapter { Index = 2, Title = "The Lost Command" },
        new Chapter { Index = 3, Title = "A Special Op" },
    ];

    // --- TextChapterFormatter ---

    [Test]
    public async Task TextChapterFormatter_FormatsCorrectly()
    {
        var formatter = new TextChapterFormatter();
        var result = formatter.Format(SampleChapters);

        await Assert.That(result).Contains("CHAPTER01=00:00:00.000");
        await Assert.That(result).Contains("CHAPTER01NAME=Dead in the Water");
        await Assert.That(result).Contains("CHAPTER02NAME=The Lost Command");
        await Assert.That(result).Contains("CHAPTER03NAME=A Special Op");
    }

    [Test]
    public async Task TextChapterFormatter_FormatName_IsText()
    {
        var formatter = new TextChapterFormatter();
        await Assert.That(formatter.FormatName).IsEqualTo("Text");
    }

    [Test]
    public async Task TextChapterFormatter_RoundTrips_WithParser()
    {
        var formatter = new TextChapterFormatter();
        var formatted = formatter.Format(SampleChapters);

        var parser = new TextChapterParser();
        var parsed = parser.TryParse(formatted, out var names);

        await Assert.That(parsed).IsTrue();
        await Assert.That(names).Count().IsEqualTo(3);
        await Assert.That(names[0]).IsEqualTo("Dead in the Water");
        await Assert.That(names[2]).IsEqualTo("A Special Op");
    }

    // --- XmlChapterFormatter ---

    [Test]
    public async Task XmlChapterFormatter_FormatsValidXml()
    {
        var formatter = new XmlChapterFormatter();
        var result = formatter.Format(SampleChapters);

        await Assert.That(result).Contains("chapterInfo");
        await Assert.That(result).Contains("Dead in the Water");
        await Assert.That(result).Contains("The Lost Command");
    }

    [Test]
    public async Task XmlChapterFormatter_FormatName_IsXml()
    {
        var formatter = new XmlChapterFormatter();
        await Assert.That(formatter.FormatName).IsEqualTo("XML");
    }

    [Test]
    public async Task XmlChapterFormatter_RoundTrips_WithParser()
    {
        var formatter = new XmlChapterFormatter();
        var formatted = formatter.Format(SampleChapters);

        var parser = new XmlChapterParser();
        var parsed = parser.TryParse(formatted, out var names);

        await Assert.That(parsed).IsTrue();
        await Assert.That(names).Count().IsEqualTo(3);
        await Assert.That(names[0]).IsEqualTo("Dead in the Water");
    }

    // --- NamesChapterFormatter ---

    [Test]
    public async Task NamesChapterFormatter_FormatsAsList()
    {
        var formatter = new NamesChapterFormatter();
        var result = formatter.Format(SampleChapters);
        var lines = result.Split(Environment.NewLine);

        await Assert.That(lines).Count().IsEqualTo(3);
        await Assert.That(lines[0]).IsEqualTo("Dead in the Water");
        await Assert.That(lines[1]).IsEqualTo("The Lost Command");
        await Assert.That(lines[2]).IsEqualTo("A Special Op");
    }

    [Test]
    public async Task NamesChapterFormatter_FormatName_IsNames()
    {
        var formatter = new NamesChapterFormatter();
        await Assert.That(formatter.FormatName).IsEqualTo("Names");
    }

    // --- ChapterFormatter (orchestrator) ---

    [Test]
    public async Task ChapterFormatter_GetFormatNames_ReturnsAll()
    {
        var names = ChapterFormatter.GetFormatNames().ToList();

        await Assert.That(names).Contains("Text");
        await Assert.That(names).Contains("XML");
        await Assert.That(names).Contains("Names");
    }

    [Test]
    public async Task ChapterFormatter_Format_WithValidName_ReturnsFormatted()
    {
        var result = ChapterFormatter.Format("Text", SampleChapters);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!).Contains("CHAPTER01NAME=Dead in the Water");
    }

    [Test]
    public async Task ChapterFormatter_Format_CaseInsensitive()
    {
        var result = ChapterFormatter.Format("text", SampleChapters);

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task ChapterFormatter_Format_WithUnknownName_ReturnsNull()
    {
        var result = ChapterFormatter.Format("Unknown", SampleChapters);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ChapterFormatter_EmptyChapters_ReturnsEmptyString()
    {
        var result = ChapterFormatter.Format("Names", new List<Chapter>());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!).IsEqualTo(string.Empty);
    }
}
