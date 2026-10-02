using System.Text.Json;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class BluRayTitleMapper
{
    internal static ManifestTitle CreateBluRayTitle(
        string path,
        MplsPlaylist playlist,
        DiscFileCatalog files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        ICollection<ManifestDiagnostic> diagnostics,
        int? part = null)
    {
        var playback = CreateSegments(path, playlist, clpiByClip, diagnostics);
        long presentationTicks = playback.DurationTicks + BluRayPlaylistRules.GetTrailingClipTicks(playlist, clpiByClip);
        var chapters = BluRayChapterMapper.CreateBluRayChapters(playlist, diagnostics, path, presentationTicks);
        var streams = ManifestStreamMapper.CreateBluRayStreams(playlist, clpiByClip);
        var stereoscopic3D = CreateStereoscopicView(playlist, diagnostics, path);
        if (part is null or 0)
        {
            AddUnsupportedExtensionDiagnostics(playlist, diagnostics, path);
        }

        return new ManifestTitle
        {
            Source = new ManifestTitleSource
            {
                Path = path,
                Part = part,
            },
            DurationSeconds = ManifestTiming.TicksToSeconds(presentationTicks),
            SizeBytes = ComputeTitleSizeBytes(playback.Segments, stereoscopic3D, files),
            Chapters = chapters.Count > 0 ? chapters : null,
            Segments = playback.Segments,
            Streams = streams.Count > 0 ? streams : null,
            Stereoscopic3D = stereoscopic3D,
            Extensions = CreateUnsupportedExtensionEvidence(playlist),
        };
    }

    private static PlaylistPlayback CreateSegments(
        string path,
        MplsPlaylist playlist,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        long cumulativeTicks = 0;
        var segments = new List<ManifestSegment>(playlist.PlayItems.Count);
        var dependentClipByPlayItem = playlist.StereoVideoRelationships
            .GroupBy(relationship => relationship.BasePlayItemIndex)
            .ToDictionary(group => group.Key, group => group.First().DependentClipId);
        MplsPlayItem? previous = null;
        foreach (var item in playlist.PlayItems.OrderBy(item => item.Index))
        {
            long durationTicks = item.OutTime >= item.InTime ? item.OutTime - item.InTime : 0;

            // A play item that resumes its clip exactly where the previous play item stopped
            // continues that segment: MakeMKV reports the Monsters University bonus disc's
            // 00100.mpls, 366 consecutive slices of clip 914, as the one segment 914.
            if (previous is not null
                && item.Clips.Count <= 1
                && previous.Clips.Count <= 1
                && string.Equals(item.ClipId, previous.ClipId, StringComparison.OrdinalIgnoreCase)
                && item.InTime == previous.OutTime
                && !dependentClipByPlayItem.ContainsKey(item.Index)
                && !dependentClipByPlayItem.ContainsKey(previous.Index))
            {
                segments[^1] = segments[^1] with
                {
                    DurationSeconds = ManifestTiming.TicksToSeconds(cumulativeTicks + durationTicks) - (segments[^1].StartSeconds ?? 0),
                };
                cumulativeTicks += durationTicks;
                previous = item;
                continue;
            }

            previous = item;
            var clips = item.Clips.Count > 0
                ? item.Clips
                : new[]
                {
                    new MplsClipReference
                    {
                        ClipId = item.ClipId,
                        CodecId = item.CodecId,
                        StcId = item.StcId,
                    },
                };
            foreach (var clip in clips.Select((value, angleIndex) => new { value, angleIndex }))
            {
                segments.Add(new ManifestSegment
                {
                    Clip = clip.value.ClipId,
                    StartSeconds = ManifestTiming.TicksToSeconds(cumulativeTicks),
                    DurationSeconds = ManifestTiming.TicksToSeconds(durationTicks),
                    Angle = clips.Count > 1 ? clip.angleIndex + 1 : null,
                    DependentClip = clip.angleIndex == 0
                        && dependentClipByPlayItem.TryGetValue(item.Index, out string? dependentClip)
                        ? dependentClip
                        : null,
                });
            }

            cumulativeTicks += durationTicks;

            foreach (var clip in clips)
            {
                if (!clpiByClip.ContainsKey(clip.ClipId))
                {
                    diagnostics.Add(new ManifestDiagnostic
                    {
                        Severity = "warning",
                        Code = "ODM_CLPI_MISSING",
                        Message = $"Playlist references clip {clip.ClipId}, but its CLPI file was not available.",
                        Path = path,
                    });
                }
            }
        }

        return new PlaylistPlayback(segments, cumulativeTicks);
    }

    private sealed record PlaylistPlayback(IReadOnlyList<ManifestSegment> Segments, long DurationTicks);

    /// <summary>
    /// Maps the playlist's explicit stereoscopic base/dependent-view relationship (Blu-ray
    /// 3D MVC), when declared, into a title-level <see cref="ManifestStereoscopicView"/>.
    /// This represents a semantic base-view + 3D dependent-view pairing, never an
    /// alternate camera angle and never a separate logical title.
    /// </summary>
    internal static ManifestStereoscopicView? CreateStereoscopicView(
        MplsPlaylist playlist,
        ICollection<ManifestDiagnostic> diagnostics,
        string path)
    {
        if (playlist.StereoVideoRelationships.Count == 0)
        {
            return null;
        }

        if (playlist.StereoVideoRelationships.Count > 1)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_3D_MULTIPLE_RELATIONSHIPS",
                Message = $"Playlist declares {playlist.StereoVideoRelationships.Count} stereoscopic base/dependent-view relationships; only the first is represented on this title.",
                Path = path,
            });
        }

        var relationship = playlist.StereoVideoRelationships[0];
        return new ManifestStereoscopicView
        {
            RelationshipType = relationship.RelationshipType,
            BaseClipId = relationship.BaseClipId,
            BaseCodecId = relationship.BaseCodecId,
            BaseStcId = relationship.BaseStcId,
            DependentClipId = relationship.DependentClipId,
            DependentCodecId = relationship.DependentCodecId,
            DependentStcId = relationship.DependentStcId,
            SyncPlayItemId = relationship.SyncPlayItemId,
            SyncPresentationTimestampTicks45k = relationship.SyncPresentationTimestamp,
            IsSsVideoSubPath = relationship.IsSsVideoSubPath,
            DependentStream = relationship.DependentViewStream is { } stream
                ? new ManifestStereoscopicDependentStream
                {
                    Pid = stream.Pid,
                    CodingTypeCode = stream.CodingTypeCode,
                    Codec = stream.CodingType,
                    FormatCode = stream.FormatCode,
                    RateCode = stream.RateCode,
                }
                : null,
        };
    }

    /// <summary>
    /// Sums observed stream-file sizes (from file-size metadata only, never by reading
    /// payload bytes) for every clip referenced by the title's segments plus, for a
    /// stereoscopic 3D title, the MVC dependent-view clip. Accounts for both the base
    /// clip and the dependent clip without requiring both to be present in the file
    /// inventory: returns a best-effort partial sum, or <see langword="null"/> when no
    /// backing stream file is found for any referenced clip.
    /// </summary>
    private static long? ComputeTitleSizeBytes(
        IReadOnlyList<ManifestSegment> segments,
        ManifestStereoscopicView? stereoscopic3D,
        DiscFileCatalog files)
    {
        var clipIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in segments)
        {
            clipIds.Add(segment.Clip);
            if (segment.DependentClip is not null)
            {
                clipIds.Add(segment.DependentClip);
            }
        }

        if (stereoscopic3D is not null)
        {
            clipIds.Add(stereoscopic3D.DependentClipId);
        }

        long? total = null;
        foreach (var clipId in clipIds)
        {
            var file = DiscFileCatalog.Find(files, $"BDMV/STREAM/{clipId}.m2ts");
            if (file is null)
            {
                continue;
            }

            total = (total ?? 0) + file.File.Size;
        }

        return total;
    }

    /// <summary>
    /// Adds a truthful, non-guessing diagnostic for MPLS extension entries the parser
    /// preserved but does not interpret (for example, Avatar's <c>3.5</c> extension type).
    /// The mapper never infers semantics for unsupported extension entries.
    /// </summary>
    internal static void AddUnsupportedExtensionDiagnostics(
        MplsPlaylist playlist,
        ICollection<ManifestDiagnostic> diagnostics,
        string path)
    {
        if (playlist.ExtensionData is null)
        {
            return;
        }

        foreach (var entry in playlist.ExtensionData.Entries.Where(entry => !entry.IsSupported))
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_EXTENSION_UNSUPPORTED",
                Message = $"Playlist declares unsupported MPLS extension entry type {entry.TypeIdentifier}.{entry.VersionIdentifier}; its semantics are not represented in the manifest.",
                Path = path,
            });
        }
    }

    private static IReadOnlyDictionary<string, JsonElement>? CreateUnsupportedExtensionEvidence(
        MplsPlaylist playlist)
    {
        var unsupportedEntries = playlist.ExtensionData?.Entries
            .Where(entry => !entry.IsSupported)
            .ToArray();
        if (unsupportedEntries is not { Length: > 0 })
        {
            return null;
        }

        const string extensionKey = "thediscdb.optical-disc-manifest/unsupported-mpls-extensions";
        var evidence = unsupportedEntries.Select(entry => new
        {
            typeIdentifier = entry.TypeIdentifier,
            versionIdentifier = entry.VersionIdentifier,
            name = entry.Name,
            relativeStartAddress = entry.RelativeStartAddress,
            length = entry.Length,
            overlapsAnotherEntry = entry.OverlapsAnotherEntry,
            rawDataHex = entry.RawDataHex,
            isRawDataTruncated = entry.IsRawDataTruncated,
        });

        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [extensionKey] = JsonSerializer.SerializeToElement(evidence),
        };
    }
}
