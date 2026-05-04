namespace TheDiscDb.Chapters;

using TheDiscDb.InputModels;

/// <summary>
/// Provides access to all registered chapter formatters.
/// </summary>
public static class ChapterFormatter
{
    private static readonly IChapterFormatter[] Formatters =
    [
        new TextChapterFormatter(),
        new XmlChapterFormatter(),
        new NamesChapterFormatter(),
    ];

    /// <summary>
    /// Gets all available format names.
    /// </summary>
    public static IEnumerable<string> GetFormatNames() => Formatters.Select(f => f.FormatName);

    /// <summary>
    /// Formats chapters using the specified format name.
    /// </summary>
    public static string? Format(string formatName, IEnumerable<Chapter> chapters)
    {
        var formatter = Formatters.FirstOrDefault(f =>
            string.Equals(f.FormatName, formatName, StringComparison.OrdinalIgnoreCase));

        return formatter?.Format(chapters);
    }
}
