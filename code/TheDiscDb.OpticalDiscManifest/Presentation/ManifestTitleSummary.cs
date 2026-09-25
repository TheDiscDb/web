using TheDiscDb.OpticalDiscManifest.Models;

namespace TheDiscDb.OpticalDiscManifest.Presentation;

/// <summary>
/// Builds a human-scannable summary view of a manifest's titles. A disc can enumerate
/// dozens of playlists (menus, trailers, per-chapter loops, etc.) alongside the main
/// feature, so this orders titles with the largest observed size first, which is the
/// most reliable signal for surfacing the main movie without reading any payload bytes.
/// This is purely a presentation helper: it never re-derives or infers values, only
/// formats and orders fields that already exist on <see cref="ManifestTitle"/>.
/// </summary>
public static class ManifestTitleSummaryBuilder
{
    public static IReadOnlyList<ManifestTitleSummaryRow> Build(IReadOnlyList<ManifestTitle>? titles)
    {
        if (titles is null || titles.Count == 0)
        {
            return [];
        }

        return titles
            .Select(ToRow)
            .OrderByDescending(row => row.SizeBytes ?? -1)
            .ThenByDescending(row => row.DurationSeconds ?? -1)
            .ThenBy(row => row.Index)
            .ToArray();
    }

    private static ManifestTitleSummaryRow ToRow(ManifestTitle title)
    {
        var stereo = title.Stereoscopic3D;
        string? stereoSummary = stereo is null
            ? null
            : $"base {stereo.BaseClipId} \u2192 dependent {stereo.DependentClipId} (MVC)";

        return new ManifestTitleSummaryRow
        {
            Index = title.Index,
            SourcePath = title.Source.Path ?? title.Source.Label ?? title.Source.Kind,
            DurationSeconds = title.DurationSeconds,
            DurationDisplay = ManifestSummaryFormatting.FormatDuration(title.DurationSeconds),
            ChapterCount = title.ChapterCount,
            SizeBytes = title.SizeBytes,
            SizeDisplay = ManifestSummaryFormatting.FormatSize(title.SizeBytes),
            Is3D = stereo is not null,
            StereoscopicSummary = stereoSummary,
            SegmentClipIds = title.Segments is { Count: > 0 }
                ? string.Join(", ", title.Segments.Select(segment => segment.Clip))
                : null,
        };
    }
}

/// <summary>
/// A single formatted, orderable row describing a <see cref="ManifestTitle"/> for
/// display purposes (e.g. in the optical disc manifest workbench). All raw values are
/// carried alongside their formatted display strings so callers can re-sort or filter
/// without re-parsing display text.
/// </summary>
public sealed record ManifestTitleSummaryRow
{
    public required int Index { get; init; }

    public required string SourcePath { get; init; }

    public double? DurationSeconds { get; init; }

    public string? DurationDisplay { get; init; }

    public int? ChapterCount { get; init; }

    public long? SizeBytes { get; init; }

    public string? SizeDisplay { get; init; }

    public required bool Is3D { get; init; }

    public string? StereoscopicSummary { get; init; }

    public string? SegmentClipIds { get; init; }
}

/// <summary>
/// Builds a human-scannable summary view of a manifest's CLPI-backed standalone clip
/// candidates (<see cref="ManifestDisc.Clips"/>), ordered largest-first for the same
/// reason as <see cref="ManifestTitleSummaryBuilder"/>.
/// </summary>
public static class ManifestClipSummaryBuilder
{
    public static IReadOnlyList<ManifestClipSummaryRow> Build(IReadOnlyList<ManifestClip>? clips)
    {
        if (clips is null || clips.Count == 0)
        {
            return [];
        }

        return clips
            .Select(ToRow)
            .OrderByDescending(row => row.SizeBytes ?? -1)
            .ThenByDescending(row => row.DurationSeconds ?? -1)
            .ThenBy(row => row.ClipId, StringComparer.Ordinal)
            .ToArray();
    }

    private static ManifestClipSummaryRow ToRow(ManifestClip clip)
    {
        return new ManifestClipSummaryRow
        {
            ClipId = clip.ClipId,
            StreamPath = clip.StreamPath,
            DurationSeconds = clip.DurationSeconds,
            DurationDisplay = ManifestSummaryFormatting.FormatDuration(clip.DurationSeconds),
            SizeBytes = clip.SizeBytes,
            SizeDisplay = ManifestSummaryFormatting.FormatSize(clip.SizeBytes),
            StreamCount = clip.Streams?.Count ?? 0,
        };
    }
}

public sealed record ManifestClipSummaryRow
{
    public required string ClipId { get; init; }

    public string? StreamPath { get; init; }

    public double? DurationSeconds { get; init; }

    public string? DurationDisplay { get; init; }

    public long? SizeBytes { get; init; }

    public string? SizeDisplay { get; init; }

    public required int StreamCount { get; init; }
}

internal static class ManifestSummaryFormatting
{
    public static string? FormatDuration(double? seconds)
    {
        if (seconds is null || seconds < 0)
        {
            return null;
        }

        var span = TimeSpan.FromSeconds(seconds.Value);
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss")
            : span.ToString(@"m\:ss");
    }

    public static string? FormatSize(long? bytes)
    {
        if (bytes is null || bytes < 0)
        {
            return null;
        }

        const double Gib = 1024d * 1024 * 1024;
        return $"{bytes.Value / Gib:N2} GiB";
    }
}
