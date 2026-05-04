namespace TheDiscDb.Chapters;

using TheDiscDb.InputModels;

/// <summary>
/// Formats a list of chapters into a specific text format.
/// </summary>
public interface IChapterFormatter
{
    /// <summary>
    /// Display name for this format (shown in UI dropdowns).
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// Formats the given chapters into a string representation.
    /// </summary>
    string Format(IEnumerable<Chapter> chapters);
}
