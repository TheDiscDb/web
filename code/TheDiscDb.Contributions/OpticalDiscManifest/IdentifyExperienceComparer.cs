using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MakeMkv;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

[JsonConverter(typeof(JsonStringEnumConverter<IdentifyExperienceStatus>))]
public enum IdentifyExperienceStatus
{
    NotComparable,
    Match,
    Mismatch
}

public sealed record IdentifyExperienceDifference(int? Row, string Field, string? LogValue, string? ManifestValue);

public sealed record IdentifyExperienceComparison(
    int Version,
    IdentifyExperienceStatus Status,
    int LogRowCount,
    int ManifestRowCount,
    int MatchingRowCount,
    IReadOnlyList<IdentifyExperienceDifference> Differences,
    string? Reason = null)
{
    public const int CurrentVersion = 1;
    public const string ScopeText = "Default ordered identify table, full segment-map tooltip, comment column, "
        + "labeling availability after identification, and ordered audio/subtitle dialog names. "
        + "Excludes saved identifications/labels, user sorting/filtering, external chapter suggestions, clipboard state, and visual layout.";

    public string Scope => ScopeText;

    public static IdentifyExperienceComparison NotComparable(string reason)
        => new(CurrentVersion, IdentifyExperienceStatus.NotComparable, 0, 0, 0, [], reason);
}

public static class IdentifyExperienceComparer
{
    public static IdentifyExperienceComparison Compare(DiscInfo log, DiscInfo scan)
    {
        if (log.Titles.Count == 0 || scan.Titles.Count == 0)
        {
            return IdentifyExperienceComparison.NotComparable("Both acquisitions must contain identify rows.");
        }

        var differences = new List<IdentifyExperienceDifference>();
        bool logComments = IdentifyTitlePresentation.HasCommentColumn(log.Titles.Select(t => t.JavaComment));
        bool scanComments = IdentifyTitlePresentation.HasCommentColumn(scan.Titles.Select(t => t.JavaComment));
        Add(null, "CommentColumnVisible", logComments.ToString(), scanComments.ToString());
        Add(null, "RowCount", Number(log.Titles.Count), Number(scan.Titles.Count));
        int matchingRows = 0;
        for (int index = 0; index < Math.Max(log.Titles.Count, scan.Titles.Count); index++)
        {
            int row = index + 1;
            if (index >= log.Titles.Count || index >= scan.Titles.Count)
            {
                Add(row, "Row", index < log.Titles.Count ? log.Titles[index].Playlist ?? "" : null,
                    index < scan.Titles.Count ? scan.Titles[index].Playlist ?? "" : null);
                continue;
            }

            Title left = log.Titles[index];
            Title right = scan.Titles[index];
            int before = differences.Count;
            Add(row, "ChapterCount", Number(left.ChapterCount), Number(right.ChapterCount));
            Add(row, "DisplaySize", left.DisplaySize ?? "", right.DisplaySize ?? "");
            Add(row, "Length", left.Length ?? "", right.Length ?? "");
            Add(row, "Playlist", left.Playlist ?? "", right.Playlist ?? "");
            Add(row, "SegmentMap", IdentifyTitlePresentation.SegmentMapText(left.SegmentMap), IdentifyTitlePresentation.SegmentMapText(right.SegmentMap));
            Add(row, "SegmentMapTooltip", left.SegmentMap ?? "", right.SegmentMap ?? "");
            Add(row, "Comment", logComments ? left.JavaComment ?? "" : "", scanComments ? right.JavaComment ?? "" : "");
            Add(row, "LabelChapters", IdentifyTitlePresentation.CanLabelChapters(left.ChapterCount).ToString(),
                IdentifyTitlePresentation.CanLabelChapters(right.ChapterCount).ToString());
            CompareTracks(row, "Audio", left, right, IdentifyTitlePresentation.IsAudio,
                segment => IdentifyTitlePresentation.AudioName(segment.Name, segment.AudioType));
            CompareTracks(row, "Subtitles", left, right, IdentifyTitlePresentation.IsSubtitle,
                segment => IdentifyTitlePresentation.SubtitleName(segment.Name));
            if (before == differences.Count)
            {
                matchingRows++;
            }
        }

        return new(IdentifyExperienceComparison.CurrentVersion,
            differences.Count == 0 ? IdentifyExperienceStatus.Match : IdentifyExperienceStatus.Mismatch,
            log.Titles.Count, scan.Titles.Count, matchingRows, differences);

        void Add(int? row, string field, string? left, string? right)
        {
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                differences.Add(new(row, field, left, right));
            }
        }

        void CompareTracks(int row, string field, Title left, Title right, Func<string?, bool> predicate, Func<Segment, string> name)
        {
            var leftNames = left.Segments.Where(s => predicate(s.Type)).Select(name).ToArray();
            var rightNames = right.Segments.Where(s => predicate(s.Type)).Select(name).ToArray();
            Add(row, $"Label{field}", (leftNames.Length > 0).ToString(), (rightNames.Length > 0).ToString());
            if (!leftNames.SequenceEqual(rightNames, StringComparer.Ordinal))
            {
                Add(row, $"{field}DialogNames", JsonSerializer.Serialize(leftNames), JsonSerializer.Serialize(rightNames));
            }
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
