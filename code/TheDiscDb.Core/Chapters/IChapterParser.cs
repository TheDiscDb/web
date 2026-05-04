namespace TheDiscDb.Chapters;

/// <summary>
/// Parses chapter names from a specific text format.
/// </summary>
public interface IChapterParser
{
    /// <summary>
    /// Tries to parse chapter names from the given text.
    /// </summary>
    bool TryParse(string text, out List<string> chapterNames);
}
