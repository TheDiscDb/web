namespace MakeMkv;

public static class IdentifyTitlePresentation
{
    public static string SegmentMapText(string? text)
        => string.IsNullOrEmpty(text) ? "" : text.Length <= 12 ? text : text[..12] + "...";

    public static bool IsAudio(string? type) => string.Equals(type, "Audio", StringComparison.OrdinalIgnoreCase);

    public static bool IsSubtitle(string? type) => string.Equals(type, "Subtitles", StringComparison.OrdinalIgnoreCase);

    public static bool CanLabelChapters(int chapterCount) => chapterCount > 1;

    public static bool HasCommentColumn(IEnumerable<string?> comments) => comments.Any(comment => !string.IsNullOrEmpty(comment));

    public static string AudioName(string? name, string? audioType) => $"{name} {audioType}";

    public static string SubtitleName(string? name) => $"{name}".Trim();
}
