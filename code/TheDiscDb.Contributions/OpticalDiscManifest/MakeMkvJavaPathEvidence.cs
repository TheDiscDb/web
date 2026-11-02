using System.Globalization;
using System.Text.Json.Serialization;
using MakeMkv;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

[JsonConverter(typeof(JsonStringEnumConverter<MakeMkvJavaPathStatus>))]
public enum MakeMkvJavaPathStatus
{
    Unknown,
    NotUsed,
    Failed,
    Used
}

public sealed record MakeMkvJavaPathEvidence(
    MakeMkvJavaPathStatus Status,
    string Reason,
    IReadOnlyList<string> RenamedRows,
    IReadOnlyList<string> RemovedRows)
{
    public bool Normalized => RenamedRows.Count > 0 || RemovedRows.Count > 0;
}

/// <summary>
/// Removes reference differences caused by MakeMKV's Java (BD-J) path, which the scanner never emulates.
/// When the JVM runs, MakeMKV names whole single-clip titles after the playlist and can list a playlist and its
/// clip twice; without it the same title is named after the clip once. Only the identify comparison uses the
/// normalized log; the raw reference is never modified.
/// </summary>
public static class MakeMkvJavaPathNormalizer
{
    private const string JavaUsedCode = "3344";
    private const string JavaFailedCode = "4041";

    public static MakeMkvJavaPathStatus Detect(string logText)
    {
        ArgumentNullException.ThrowIfNull(logText);

        bool messages = false;
        bool used = false;
        bool failed = false;
        foreach (string line in logText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("MSG:", StringComparison.Ordinal))
            {
                continue;
            }

            messages = true;
            string? code = new CsvEnumerator(line[4..]).GetString();
            used |= code == JavaUsedCode;
            failed |= code == JavaFailedCode;
        }

        return !messages ? MakeMkvJavaPathStatus.Unknown
            : failed ? MakeMkvJavaPathStatus.Failed
            : used ? MakeMkvJavaPathStatus.Used
            : MakeMkvJavaPathStatus.NotUsed;
    }

    public static (DiscInfo Log, MakeMkvJavaPathEvidence Evidence) Normalize(string logText, DiscInfo log, DiscInfo scan)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(scan);

        var status = Detect(logText);
        if (status != MakeMkvJavaPathStatus.Used)
        {
            string reason = status switch
            {
                MakeMkvJavaPathStatus.Unknown => "The log contains no MSG rows; Java runtime use cannot be determined.",
                MakeMkvJavaPathStatus.Failed => "MakeMKV could not launch the Java runtime (MSG:4041).",
                _ => "MakeMKV did not use the Java runtime."
            };
            return (log, new(status, reason, [], []));
        }

        var scanSources = scan.Titles
            .Where(title => title.Playlist is not null)
            .ToLookup(title => title.Playlist!, StringComparer.OrdinalIgnoreCase);
        var streamRows = new HashSet<string>(
            log.Titles.Select(title => title.Playlist).OfType<string>()
                .Where(source => source.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        var renamed = new List<string>();
        var removed = new List<string>();
        var titles = new List<Title>(log.Titles.Count);
        for (int index = 0; index < log.Titles.Count; index++)
        {
            Title title = log.Titles[index];
            string row = (index + 1).ToString(CultureInfo.InvariantCulture);
            string? stream = WholeClipStream(title, scanSources);
            if (stream is null)
            {
                titles.Add(Copy(title, title.Playlist));
            }
            else if (!streamRows.Add(stream))
            {
                removed.Add($"Row {row}: {title.Playlist} duplicates {stream}");
            }
            else
            {
                renamed.Add($"Row {row}: {title.Playlist} -> {stream}");
                // MakeMKV lists every stream (.m2ts) title without chapters.
                titles.Add(Copy(title, stream, chapterCount: 0));
            }
        }

        var normalized = new DiscInfo
        {
            Name = log.Name,
            Type = log.Type,
            LanguageCode = log.LanguageCode,
            Language = log.Language,
            SourceKind = log.SourceKind,
            Titles = titles,
            HashInfo = log.HashInfo
        };
        return (normalized, new(status,
            "MakeMKV used the Java runtime (MSG:3344); Java-only playlist names, duplicate rows and comments are ignored.",
            renamed, removed));
    }

    // A playlist row is the Java-path name of a stream when it plays exactly one whole clip: its segment map is one
    // clip, the scan lists that clip with the same byte size, and the scan does not list the playlist itself.
    private static string? WholeClipStream(Title title, ILookup<string, Title> scanSources)
    {
        if (title.Playlist is not { } playlist
            || !playlist.EndsWith(".mpls", StringComparison.OrdinalIgnoreCase)
            || scanSources.Contains(playlist)
            || !int.TryParse(title.SegmentMap?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int clip)
            || clip > 99999)
        {
            return null;
        }

        string stream = clip.ToString("D5", CultureInfo.InvariantCulture) + ".m2ts";
        return scanSources[stream].Any(candidate => candidate.Size > 0 && candidate.Size == title.Size) ? stream : null;
    }

    private static Title Copy(Title title, string? playlist, int? chapterCount = null) => new()
    {
        Index = title.Index,
        ChapterCount = chapterCount ?? title.ChapterCount,
        Length = title.Length,
        DisplaySize = title.DisplaySize,
        Size = title.Size,
        Playlist = playlist,
        SegmentMap = title.SegmentMap,
        Comment = title.Comment,
        JavaComment = null,
        Segments = title.Segments
    };
}
