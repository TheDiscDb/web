namespace TheDiscDb.Chapters;

using System.Text.RegularExpressions;

/// <summary>
/// Parses chapter names from the ChapterGrabber text format.
/// Lines matching CHAPTER##NAME=... are extracted.
/// </summary>
public class TextChapterParser : IChapterParser
{
    private static readonly Regex ChapterNamePattern = new Regex(
        @"^CHAPTER\d+NAME=(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public bool TryParse(string text, out List<string> chapterNames)
    {
        chapterNames = [];

        var matches = ChapterNamePattern.Matches(text);
        if (matches.Count == 0)
        {
            return false;
        }

        foreach (Match match in matches)
        {
            chapterNames.Add(match.Groups[1].Value.Trim());
        }

        return true;
    }
}
