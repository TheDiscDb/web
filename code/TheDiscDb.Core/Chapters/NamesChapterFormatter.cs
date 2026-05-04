namespace TheDiscDb.Chapters;

using TheDiscDb.InputModels;

/// <summary>
/// Formats chapters as a plain newline-separated list of names.
/// </summary>
public class NamesChapterFormatter : IChapterFormatter
{
    public string FormatName => "Names";

    public string Format(IEnumerable<Chapter> chapters)
    {
        return string.Join(Environment.NewLine, chapters.Select(c => c.Title ?? string.Empty));
    }
}
