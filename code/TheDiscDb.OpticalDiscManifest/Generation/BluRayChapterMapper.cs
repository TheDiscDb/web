using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class BluRayChapterMapper
{
    /// <summary>
    /// Minimum ticks (45 kHz) between a final entry mark and playlist end still treated
    /// as a MakeMKV-style terminal chapter-end sentinel. The mark must lie strictly
    /// before playlist end; a mark exactly at the end is not a sentinel candidate.
    /// </summary>
    internal const long TerminalSentinelMinimumTicksFromEnd = 1;

    /// <summary>
    /// Maximum ticks (45 kHz) between a final entry mark and playlist end still treated
    /// as a terminal chapter-end sentinel: one half second (22,500 ticks). MakeMKV drops
    /// a final chapter this short. Real-disc evidence: Knives Out 1080p 1,876 ticks;
    /// Spartacus (UHD) and Avatar (UHD) 11,261 ticks; Avengers: Age of Ultron 3D
    /// 11,262 ticks; 2001: A Space Odyssey (UHD) 15,014 ticks.
    /// </summary>
    internal const long TerminalSentinelMaximumTicksFromEnd = 22_500;

    /// <summary>
    /// Minimum authored start time for a final entry mark to be eligible for terminal
    /// sentinel removal. Real Age of Ultron 2D sub-second playlists (00050-00052,
    /// 00054-00057) place a legitimate second chapter 3,753 ticks into a 15,014-tick
    /// playlist (11,261 ticks before the end); requiring at least one second of
    /// preceding content preserves those chapters while retaining the known long-title
    /// sentinel behavior.
    /// </summary>
    internal const long TerminalSentinelMinimumStartTicks = 45_000;

    /// <summary>
    /// Determines whether a final entry mark's distance from playlist end falls within
    /// the terminal chapter-end sentinel tolerance band (strictly before end, at most 0.5 s).
    /// </summary>
    internal static bool IsTerminalChapterSentinel(long ticksFromEnd)
        => ticksFromEnd >= TerminalSentinelMinimumTicksFromEnd
            && ticksFromEnd <= TerminalSentinelMaximumTicksFromEnd;

    internal static bool ShouldExcludeTerminalChapterSentinel(long startTicks, long ticksFromEnd)
        => startTicks >= TerminalSentinelMinimumStartTicks
            && IsTerminalChapterSentinel(ticksFromEnd);

    /// <summary>
    /// Maps authored MPLS playlist marks to chapters. Only <see cref="MplsPlaylistMark.IsEntryMark"/>
    /// marks are emitted as chapters (link marks are never chapters). A final entry mark
    /// that lands within the terminal chapter-end sentinel tolerance
    /// (<see cref="TerminalSentinelMinimumTicksFromEnd"/>..<see cref="TerminalSentinelMaximumTicksFromEnd"/>
    /// ticks before playlist end) is excluded as a MakeMKV-style terminal sentinel, not a
    /// legitimate chapter. Parser-authored marks themselves are never mutated -- only
    /// which marks are converted into manifest chapters is affected.
    /// </summary>
    internal static IReadOnlyList<ManifestChapter> CreateBluRayChapters(
        MplsPlaylist playlist,
        ICollection<ManifestDiagnostic> diagnostics,
        string path,
        long? presentationTicks45k = null)
    {
        var itemStarts = new long[playlist.PlayItems.Count];
        long cumulative = 0;
        foreach (var item in playlist.PlayItems.OrderBy(item => item.Index))
        {
            if (item.Index >= 0 && item.Index < itemStarts.Length)
            {
                itemStarts[item.Index] = cumulative;
            }

            cumulative += item.OutTime >= item.InTime ? item.OutTime - item.InTime : 0;
        }

        long totalDurationTicks45k = Math.Max(cumulative, presentationTicks45k ?? 0);

        var resolved = new List<(MplsPlaylistMark Mark, long StartTicks)>();
        foreach (var mark in playlist.Marks.Where(item => item.IsEntryMark).OrderBy(item => item.Index))
        {
            if (mark.PlayItemReference < 0 || mark.PlayItemReference >= playlist.PlayItems.Count)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "warning",
                    Code = "ODM_MARK_REFERENCE_INVALID",
                    Message = $"Playlist mark {mark.Index} references missing play item {mark.PlayItemReference}.",
                    Path = path,
                });
                continue;
            }

            var playItem = playlist.PlayItems[mark.PlayItemReference];
            long relativeTicks = mark.Time >= playItem.InTime ? mark.Time - playItem.InTime : 0;
            long startTicks = itemStarts[mark.PlayItemReference] + relativeTicks;
            resolved.Add((mark, startTicks));
        }

        var chapters = new List<ManifestChapter>(resolved.Count);
        var chapterStartTicks = new List<long>(resolved.Count);
        for (int i = 0; i < resolved.Count; i++)
        {
            var (mark, startTicks) = resolved[i];
            if (i == resolved.Count - 1)
            {
                long ticksFromEnd = totalDurationTicks45k - startTicks;
                if (ShouldExcludeTerminalChapterSentinel(startTicks, ticksFromEnd))
                {
                    diagnostics.Add(new ManifestDiagnostic
                    {
                        Severity = "info",
                        Code = "ODM_BD_CHAPTER_TERMINAL_SENTINEL_EXCLUDED",
                        Message = $"Excluded a final entry mark {ticksFromEnd} ticks (45 kHz) before playlist end as a MakeMKV-style terminal chapter-end sentinel, not a legitimate chapter.",
                        Path = path,
                    });
                    continue;
                }
            }

            chapters.Add(new ManifestChapter
            {
                StartSeconds = ManifestTiming.TicksToSeconds(startTicks),
            });
            chapterStartTicks.Add(startTicks);
        }

        for (int i = 0; i < chapters.Count; i++)
        {
            long chapterEndTicks = i + 1 < chapters.Count
                ? chapterStartTicks[i + 1]
                : totalDurationTicks45k;
            long durationTicks = Math.Max(0, chapterEndTicks - chapterStartTicks[i]);
            chapters[i] = chapters[i] with { DurationSeconds = ManifestTiming.TicksToSeconds(durationTicks) };
        }

        return chapters;
    }
}
