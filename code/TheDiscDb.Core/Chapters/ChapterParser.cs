namespace TheDiscDb.Chapters;

/// <summary>
/// Tries each registered <see cref="IChapterParser"/> in order until one succeeds.
/// </summary>
public static class ChapterParser
{
    private static readonly IChapterParser[] Parsers =
    [
        new TextChapterParser(),
        new XmlChapterParser(),
    ];

    /// <summary>
    /// Tries to parse chapter names from text using all registered parsers.
    /// </summary>
    public static bool TryParseChapters(string text, out List<string> chapterNames)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            chapterNames = [];
            return false;
        }

        foreach (var parser in Parsers)
        {
            if (parser.TryParse(text, out chapterNames))
            {
                return true;
            }
        }

        chapterNames = [];
        return false;
    }
}
