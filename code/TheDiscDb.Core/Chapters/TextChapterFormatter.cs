namespace TheDiscDb.Chapters;

using System.Text;
using TheDiscDb.InputModels;

/// <summary>
/// Formats chapters as ChapterGrabber text format.
/// </summary>
public class TextChapterFormatter : IChapterFormatter
{
    public string FormatName => "Text";

    public string Format(IEnumerable<Chapter> chapters)
    {
        var sb = new StringBuilder();
        foreach (var chapter in chapters)
        {
            string index = chapter.Index.ToString("D2");
            sb.AppendLine($"CHAPTER{index}=00:00:00.000");
            sb.AppendLine($"CHAPTER{index}NAME={chapter.Title}");
        }

        return sb.ToString().TrimEnd();
    }
}
