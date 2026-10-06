using System.Globalization;
using System.Text;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class BluRayTitleReconciler
{
    /// <summary>
    /// Aligns the playlist-derived title list with the set of playback candidates an
    /// optical disc actually offers, so a manifest produced from the disc describes the
    /// same titles a MakeMKV log of that disc would describe. Reconciliations run,
    /// in order: a playlist that plays exactly like another and selects no stream the
    /// other lacks collapses into it (the earlier one wins when both select the same
    /// streams); stream files no retained playlist references, or that a Dolby Vision
    /// playlist or a playlist holding a still plays,
    /// are promoted to titles of their own, except clips a non-Dolby-Vision subpath (such
    /// as a popup menu) plays, which are never playback candidates; and a title whose only content is a single unchaptered clip,
    /// played in full, is attributed to that clip's stream file, because the playlist adds
    /// nothing the stream does not already state. A chaptered single-clip playlist can
    /// also cover a standalone stream with identical duration, size and stream selection.
    /// Earlier titles win ties, so
    /// callers pass playlist titles in disc order. Each reconciliation is
    /// evidence-driven and never discards a distinct playback candidate.
    /// </summary>
    internal static List<ManifestTitle> ReconcileBluRayTitles(
        IReadOnlyList<ManifestTitle> titles,
        IReadOnlyList<NormalizedFile> files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        IReadOnlySet<string> dolbyVisionClips,
        IReadOnlySet<string> subPathClips,
        IReadOnlySet<string> stillPlaylistClips,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        var retained = RemoveEquivalentPlaylists(titles, diagnostics);
        RemoveUnchapteredPresentations(retained, diagnostics);
        var referenced = CollectReferencedClips(retained, subPathClips);
        PromoteStreamTitles(retained, files, clpiByClip, referenced, dolbyVisionClips, stillPlaylistClips, diagnostics);
        AttributeFullClipTitles(retained, files, clpiByClip, diagnostics);
        RemoveDuplicateStreamSources(retained, diagnostics);
        RemoveStreamsCoveredByChapteredPlaylists(retained, dolbyVisionClips, stillPlaylistClips, diagnostics);
        return retained;
    }

    private static List<ManifestTitle> RemoveEquivalentPlaylists(
        IReadOnlyList<ManifestTitle> titles,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        var retained = new List<ManifestTitle>(titles.Count);
        var compositions = new Dictionary<string, List<ManifestTitle>>(StringComparer.Ordinal);
        foreach (var title in titles)
        {
            string composition = CreateBluRayCompositionKey(title);
            if (!compositions.TryGetValue(composition, out var samePlayback))
            {
                compositions[composition] = samePlayback = [];
            }

            var original = samePlayback.FirstOrDefault(earlier => SelectsNoStreamBeyond(title, earlier));
            if (original is not null)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "info",
                    Code = "ODM_BD_TITLE_DUPLICATE",
                    Message = $"Playlist plays the same as {original.Source?.Path ?? "an earlier playlist"} and selects no stream it lacks, so it is not emitted as a separate title.",
                    Path = title.Source?.Path,
                });
                continue;
            }

            // MakeMKV keeps the playlist selecting more streams even when it comes later:
            // on Monsters University (UHD) 00004.mpls (HEVC and one AC-3 track) is reported
            // as equal to the later 00800.mpls, which plays the same clips with every track.
            foreach (var narrower in samePlayback.Where(earlier => SelectsNoStreamBeyond(earlier, title)).ToArray())
            {
                samePlayback.Remove(narrower);
                retained.Remove(narrower);
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "info",
                    Code = "ODM_BD_TITLE_DUPLICATE",
                    Message = $"Playlist plays the same as {title.Source?.Path ?? "a later playlist"} and selects no stream it lacks, so it is not emitted as a separate title.",
                    Path = narrower.Source?.Path,
                });
            }

            samePlayback.Add(title);
            retained.Add(title);
        }

        return retained;
    }

    private static void RemoveUnchapteredPresentations(
        List<ManifestTitle> retained,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        // An unchaptered playlist that plays like a chaptered one, and selects no stream
        // it lacks, is the chaptered presentation without its chapters. MakeMKV skips it
        // even when it comes first: on Super Mario Bros (3D Blu-ray) 00006.mpls (one
        // chapter) is reported as equal to the later 01005.mpls (35 chapters). No listed
        // pair in the TheDiscDb log corpus contradicts this.
        var chaptered = retained
            .Where(title => (title.Chapters?.Count ?? 0) > 1)
            .GroupBy(title => CreateBluRayCompositionKey(title, includeChapters: false), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        retained.RemoveAll(title =>
        {
            if ((title.Chapters?.Count ?? 0) > 1
                || title.Chapters is [{ StartSeconds: > 0 }]
                || !chaptered.TryGetValue(CreateBluRayCompositionKey(title, includeChapters: false), out var candidates))
            {
                return false;
            }

            var original = candidates.FirstOrDefault(candidate => SelectsNoStreamBeyond(title, candidate));
            if (original is null)
            {
                return false;
            }

            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_TITLE_DUPLICATE",
                Message = $"Playlist plays the same as {original.Source?.Path ?? "another playlist"} without its chapters and selects no stream it lacks, so it is not emitted as a separate title.",
                Path = title.Source?.Path,
            });
            return true;
        });
    }

    private static HashSet<string> CollectReferencedClips(
        IReadOnlyList<ManifestTitle> retained,
        IReadOnlySet<string> subPathClips)
    {
        var referenced = new HashSet<string>(subPathClips, StringComparer.OrdinalIgnoreCase);
        foreach (var title in retained)
        {
            foreach (var segment in title.Segments ?? [])
            {
                referenced.Add(segment.Clip);
                if (segment.DependentClip is not null)
                {
                    referenced.Add(segment.DependentClip);
                }
            }

            // A stereoscopic dependent view is carried by the title it pairs with rather
            // than by a segment, so it is already reachable and is not its own candidate.
            if (title.Stereoscopic3D is { } view)
            {
                referenced.Add(view.BaseClipId);
                referenced.Add(view.DependentClipId);
            }
        }

        return referenced;
    }

    private static void PromoteStreamTitles(
        List<ManifestTitle> retained,
        IReadOnlyList<NormalizedFile> files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        IReadOnlySet<string> referenced,
        IReadOnlySet<string> dolbyVisionClips,
        IReadOnlySet<string> stillPlaylistClips,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        var promoted = files
            .Where(item => DiscFileCatalog.IsControlFile(item.Path, "BDMV/STREAM", ".m2ts"))
            .Where(item => !referenced.Contains(Path.GetFileNameWithoutExtension(item.Path))
                || dolbyVisionClips.Contains(Path.GetFileNameWithoutExtension(item.Path))
                || stillPlaylistClips.Contains(Path.GetFileNameWithoutExtension(item.Path)))
            .Where(item => IsPlayableStreamCandidate(
                GetClipDurationSeconds(Path.GetFileNameWithoutExtension(item.Path), clpiByClip)))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var file in promoted)
        {
            string clipId = Path.GetFileNameWithoutExtension(file.Path);
            retained.Add(CreateBluRayStreamTitle(file, clipId, GetClip(clipId, clpiByClip)));
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_STREAM_TITLE_PROMOTED",
                Message = $"Promoted clip {clipId}: unreferenced={!referenced.Contains(clipId)}, Dolby Vision={dolbyVisionClips.Contains(clipId)}, still playlist={stillPlaylistClips.Contains(clipId)}, duration seconds={GetClipDurationSeconds(clipId, clpiByClip)?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.",
                Path = file.Path,
            });
        }

        if (promoted.Length > 0)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_STREAM_TITLES",
                Message = $"Promoted {promoted.Length} stream file(s) to titles because no playlist references them, a Dolby Vision playlist plays them or a playlist holding a still plays them.",
            });
        }
    }

    private static void AttributeFullClipTitles(
        List<ManifestTitle> retained,
        IReadOnlyList<NormalizedFile> files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        var streamPaths = files
            .Where(item => DiscFileCatalog.IsControlFile(item.Path, "BDMV/STREAM", ".m2ts"))
            .Select(item => item.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < retained.Count; index++)
        {
            retained[index] = AttributeSingleClipTitleToStream(
                retained[index],
                streamPaths,
                clipId => GetClipDurationSeconds(clipId, clpiByClip),
                diagnostics);
        }
    }

    private static void RemoveDuplicateStreamSources(
        List<ManifestTitle> retained,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        // Attribution can leave two titles naming the same stream file, for example a
        // playlist part and a playlist that both play one clip in full but declare
        // different streams. A stream file is one playback candidate, so only the first
        // title naming it is kept.
        var streamSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        retained.RemoveAll(title =>
        {
            string? path = title.Source?.Path;
            if (path is null
                || !path.StartsWith("BDMV/STREAM/", StringComparison.OrdinalIgnoreCase)
                || streamSources.Add(path))
            {
                return false;
            }

            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_TITLE_DUPLICATE",
                Message = $"Title plays the same stream file as an earlier title and is not emitted separately.",
                Path = path,
            });
            return true;
        });
    }

    internal static void RemoveStreamsCoveredByChapteredPlaylists(
        List<ManifestTitle> retained,
        IReadOnlySet<string> dolbyVisionClips,
        IReadOnlySet<string> stillPlaylistClips,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        var chaptered = retained.Where(title =>
            title.Source?.Path?.StartsWith("BDMV/PLAYLIST/", StringComparison.OrdinalIgnoreCase) == true
            && title.Chapters is { Count: > 1 }
            && title.Segments is [{ StartSeconds: 0, Angle: null, DependentClip: null }]
            && title.Stereoscopic3D is null
            && title.Streams is { Count: > 0 }
            && title.DurationSeconds is > 0
            && title.SizeBytes is > 0)
            .GroupBy(title => title.Segments![0].Clip, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        retained.RemoveAll(title =>
        {
            if (title.Source?.Path?.StartsWith("BDMV/STREAM/", StringComparison.OrdinalIgnoreCase) != true
                || title.Segments is not [{ StartSeconds: 0, Angle: null, DependentClip: null } segment]
                || title.Stereoscopic3D is not null
                || title.Chapters is { Count: > 0 }
                || title.Streams is not { Count: > 0 }
                || dolbyVisionClips.Contains(segment.Clip)
                || stillPlaylistClips.Contains(segment.Clip)
                || title.DurationSeconds is not > 0
                || segment.DurationSeconds != title.DurationSeconds
                || !chaptered.TryGetValue(segment.Clip, out var candidates))
            {
                return false;
            }

            // Presentation duration can include a CLPI-backed tail beyond the authored
            // segment: SpongeBob's 00255.mpls covers 01101 once that tail is included.
            var original = candidates.FirstOrDefault(candidate =>
                candidate.SizeBytes == title.SizeBytes
                && candidate.DurationSeconds == title.DurationSeconds
                && SelectsNoStreamBeyond(title, candidate));
            if (original is null)
            {
                return false;
            }

            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_TITLE_DUPLICATE",
                Message = $"Stream is covered by chaptered playlist {original.Source!.Path} with the same clip, presentation duration and size, and selects no stream it lacks; it is not emitted separately.",
                Path = title.Source.Path,
            });
            return true;
        });
    }

    /// <summary>
    /// Orders titles the way MakeMKV presents them, so contributors labelling a disc see
    /// the same sequence they know from MakeMKV. Across the TheDiscDb log corpus MakeMKV
    /// lists every playlist-sourced title before every stream-sourced title, without
    /// exception. Within each block MakeMKV follows the disc's directory order (verified
    /// against the raw UDF directory listings of 2001: A Space Odyssey's feature and bonus
    /// discs), so each block is ordered by <paramref name="enumerationIndexOf"/>, the
    /// position of the title's source file in the caller's enumeration of the disc, with
    /// the path as tie-breaker when the enumeration carries no order.
    /// </summary>
    internal static List<ManifestTitle> OrderBluRayTitles(
        IReadOnlyList<ManifestTitle> titles,
        Func<string?, int> enumerationIndexOf)
    {
        static bool IsStreamSourced(ManifestTitle title)
            => title.Source?.Path?.StartsWith("BDMV/STREAM/", StringComparison.OrdinalIgnoreCase) == true;

        return titles
            .Where(title => !IsStreamSourced(title))
            .OrderBy(title => enumerationIndexOf(title.Source?.Path))
            .Concat(titles
                .Where(IsStreamSourced)
                .OrderBy(title => enumerationIndexOf(title.Source!.Path))
                .ThenBy(title => title.Source!.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static ClpiFile? GetClip(string clipId, IReadOnlyDictionary<string, ClpiFile> clpiByClip)
        => clpiByClip.TryGetValue(clipId, out var clpi) ? clpi : null;

    private static double? GetClipDurationSeconds(
        string clipId,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip)
    {
        var summary = GetClip(clipId, clpiByClip)?.PresentationSummary;
        return summary is not null ? ManifestTiming.TicksToSeconds(summary.DurationTicks45k) : null;
    }

    /// <summary>
    /// Decides whether a stream file no playlist references is worth promoting to a
    /// title. A stream whose CLPI declares a duration no longer than a single video
    /// frame is a BD-J placeholder or filler asset rather than a playback candidate, so
    /// it stays a clip. A stream with no CLPI evidence is promoted, because there is
    /// nothing on which to rule it out.
    /// </summary>
    internal static bool IsPlayableStreamCandidate(double? clipDurationSeconds)
        => clipDurationSeconds is not { } duration || duration > SingleFrameSeconds;

    /// <summary>
    /// Builds a comparison key describing how a title plays: its ordered segments with
    /// their angle and timing, its stereoscopic pairing, and where its chapter marks
    /// fall. Streams are deliberately left out; <see cref="SelectsNoStreamBeyond"/>
    /// compares them separately, because MakeMKV only skips a same-playback playlist when
    /// its stream selection adds nothing.
    /// </summary>
    internal static string CreateBluRayCompositionKey(ManifestTitle title)
        => CreateBluRayCompositionKey(title, includeChapters: true);

    private static string CreateBluRayCompositionKey(ManifestTitle title, bool includeChapters)
    {
        var builder = new StringBuilder();
        foreach (var segment in title.Segments ?? [])
        {
            builder.Append(segment.Clip)
                .Append('@')
                .Append(segment.StartSeconds?.ToString("R", CultureInfo.InvariantCulture) ?? "-")
                .Append('+')
                .Append(segment.DurationSeconds?.ToString("R", CultureInfo.InvariantCulture) ?? "-")
                .Append('#')
                .Append(segment.Angle?.ToString(CultureInfo.InvariantCulture) ?? "-")
                .Append('/')
                .Append(segment.DependentClip ?? "-")
                .Append(';');
        }

        builder
            .Append('|')
            .Append(title.Stereoscopic3D?.DependentClipId ?? "-")
            .Append('|');

        if (!includeChapters)
        {
            return builder.ToString();
        }

        foreach (var chapter in title.Chapters ?? [])
        {
            builder.Append(chapter.StartSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decides whether a playlist that plays exactly like an earlier one selects any
    /// stream the earlier one lacks. MakeMKV reports such a playlist as "equal to" the
    /// earlier title and skips it: on 2001: A Space Odyssey (UHD) 00090.mpls and
    /// 00103.mpls are byte-identical to 00089.mpls apart from selecting 40 and 9 of its
    /// 44 streams, and both are skipped. A playlist that adds a stream (a commentary
    /// track, say) is a distinct presentation and MakeMKV lists it, as it does for the
    /// 986 kept same-playback pairs in the TheDiscDb log corpus whose stream sets are
    /// not nested. Interactive graphics (menu) streams are not compared: MakeMKV never
    /// extracts them, and on Super Mario Bros (3D Blu-ray) it reports 01009.mpls as equal
    /// to 00005.mpls although only 01009.mpls selects a menu stream.
    /// </summary>
    internal static bool SelectsNoStreamBeyond(ManifestTitle title, ManifestTitle earlier)
    {
        var available = new HashSet<string>(
            (earlier.Streams ?? []).Where(IsComparedStream).Select(CreateStreamKey),
            StringComparer.Ordinal);
        return (title.Streams ?? []).Where(IsComparedStream).All(stream => available.Contains(CreateStreamKey(stream)));
    }

    private static bool IsComparedStream(ManifestStream stream)
        => !string.Equals(stream.Type, "menu", StringComparison.Ordinal);

    private static string CreateStreamKey(ManifestStream stream)
        => string.Join(
            ':',
            stream.Type,
            stream.Codec,
            stream.Pid?.ToString(CultureInfo.InvariantCulture) ?? "-",
            stream.Language ?? "-",
            stream.Category ?? "-",
            stream.AudioLayout ?? "-");

    /// <summary>
    /// Re-attributes a title to its stream file when the playlist contributes nothing
    /// beyond the clip itself: exactly one segment that plays the whole clip, no
    /// alternate angle, no stereoscopic pairing, and at most one chapter mark. A playlist
    /// that trims its clip is a different presentation, so it keeps its playlist
    /// attribution, as MakeMKV does. When the clip's duration is unknown, coverage cannot
    /// be shown and the playlist attribution is kept. The segments are left untouched, so
    /// the clip the title plays is still stated explicitly; the chapters are dropped,
    /// because a stream file carries no chapter marks of its own.
    /// </summary>
    internal static ManifestTitle AttributeSingleClipTitleToStream(
        ManifestTitle title,
        IReadOnlySet<string> streamPaths,
        Func<string, double?> clipDurationSeconds,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        if (title.Segments is not { Count: 1 }
            || title.Stereoscopic3D is not null
            || title.Chapters is { Count: > 1 })
        {
            return title;
        }

        var segment = title.Segments[0];
        if (segment.Angle is not null)
        {
            return title;
        }

        string streamPath = $"BDMV/STREAM/{segment.Clip}.m2ts";
        if (!streamPaths.Contains(streamPath))
        {
            return title;
        }

        if (segment.DurationSeconds is not { } played
            || clipDurationSeconds(segment.Clip) is not { } clipDuration
            || Math.Abs(clipDuration - played) > SingleFrameSeconds)
        {
            return title;
        }

        if (!string.Equals(title.Source?.Path, streamPath, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_STREAM_TITLE_ATTRIBUTED",
                Message = $"Attributed {title.Source?.Path ?? "an unnamed title"} (part {title.Source?.Part?.ToString(CultureInfo.InvariantCulture) ?? "none"}) to clip {segment.Clip}: one full-clip segment, no alternate angle or stereoscopic pairing, {title.Chapters?.Count ?? 0} chapter mark(s) removed; played seconds={played.ToString(CultureInfo.InvariantCulture)}, clip seconds={clipDuration.ToString(CultureInfo.InvariantCulture)}.",
                Path = streamPath,
            });
        }

        return title with
        {
            Source = (title.Source ?? new ManifestTitleSource()) with { Path = streamPath, Part = null },
            // MakeMKV never reports chapters for a title sourced from a stream file (none
            // of the 138,304 stream titles in the TheDiscDb log corpus carry a chapter
            // count), and the at most one mark a re-attributed title can have only marks
            // the start of the clip.
            Chapters = null,
        };
    }

    /// <summary>
    /// Creates a title for a stream file that no playlist references. Its duration and
    /// streams come from the matching CLPI control file when one exists; nothing is
    /// inferred from the M2TS payload, which is never read.
    /// </summary>
    private static ManifestTitle CreateBluRayStreamTitle(
        NormalizedFile file,
        string clipId,
        ClpiFile? clpi)
    {
        double? duration = clpi?.PresentationSummary is not null
            ? ManifestTiming.TicksToSeconds(clpi.PresentationSummary.DurationTicks45k)
            : null;
        var streams = clpi is not null ? ManifestStreamMapper.CreateClpiStreams(clpi) : [];
        return new ManifestTitle
        {
            Source = new ManifestTitleSource
            {
                Path = file.Path,
            },
            DurationSeconds = duration,
            SizeBytes = file.File.Size,
            Segments =
            [
                new ManifestSegment
                {
                    Clip = clipId,
                    StartSeconds = 0,
                    DurationSeconds = duration,
                },
            ],
            Streams = streams.Count > 0 ? streams : null,
        };
    }

    /// <summary>
    /// The longest duration that can still be a single video frame, taken from the
    /// slowest frame rate a BD-ROM stream may declare (23.976 fps, one frame every
    /// ~0.0417 seconds) with a small tolerance.
    /// </summary>
    private const double SingleFrameSeconds = 0.05;
}
