using System.Xml;
using System.Xml.Linq;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Bdmv;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class BluRayManifestBuilder
{
    private static async Task<ClipEvidence> ReadClipEvidenceAsync(
        DiscFileCatalog files,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        bool isUhd = false;
        var indexResult = await ControlFileReader.ParseControlWithBackupAsync(
            files,
            "BDMV/index.bdmv",
            "BDMV/BACKUP/index.bdmv",
            diagnostics,
            metrics,
            cancellationToken,
            bytes => new BdmvParser(ControlFileReader.CreateReader(bytes)).ParseIndexAsync().AsTask());
        if (indexResult is null)
        {
            diagnostics.Add(ControlFileReader.MissingFile("BDMV/index.bdmv"));
        }
        else
        {
            isUhd |= indexResult.Value?.Version == "0300";
        }

        var movieObjectResult = await ControlFileReader.ParseControlWithBackupAsync(
            files,
            "BDMV/MovieObject.bdmv",
            "BDMV/BACKUP/MovieObject.bdmv",
            diagnostics,
            metrics,
            cancellationToken,
            bytes => new BdmvParser(ControlFileReader.CreateReader(bytes)).ParseMovieObjectsAsync().AsTask());
        if (movieObjectResult?.Value is not null)
        {
            isUhd |= movieObjectResult.Value.Version == "0300";
        }

        var clpiByClip = new Dictionary<string, ClpiFile>(StringComparer.OrdinalIgnoreCase);
        var clpiPathByClip = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GetBluRayControlCandidates(files, "BDMV/CLIPINF", "BDMV/BACKUP/CLIPINF", ".clpi"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ControlFileReader.ParseControlWithBackupAsync(
                files,
                candidate.PrimaryPath,
                candidate.BackupPath,
                diagnostics,
                metrics,
                cancellationToken,
                bytes => new ClpiParser(ControlFileReader.CreateReader(bytes)).ParseAsync().AsTask());
            if (result?.Value is not null)
            {
                var clipId = Path.GetFileNameWithoutExtension(result.Path);
                clpiByClip[clipId] = result.Value;
                clpiPathByClip[clipId] = result.Path;
                isUhd |= result.Value.Version == "0300";
            }
        }

        return new ClipEvidence(isUhd, clpiByClip, clpiPathByClip);
    }

    private static async Task<PlaylistCandidates> BuildPlaylistCandidatesAsync(
        DiscFileCatalog files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        bool isUhd = false;
        var titles = new List<ManifestTitle>();
        var dolbyVisionClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subPathClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stillPlaylistClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int playlistCandidates = 0;
        int parsedPlaylists = 0;
        foreach (var candidate in GetBluRayControlCandidates(files, "BDMV/PLAYLIST", "BDMV/BACKUP/PLAYLIST", ".mpls"))
        {
            playlistCandidates++;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ControlFileReader.ParseControlWithBackupAsync(
                files,
                candidate.PrimaryPath,
                candidate.BackupPath,
                diagnostics,
                metrics,
                cancellationToken,
                bytes => new MplsParser(ControlFileReader.CreateReader(bytes)).ParseAsync().AsTask());
            if (result?.Value is null)
            {
                continue;
            }

            parsedPlaylists++;
            isUhd |= result.Value.Version == "0300";
            BluRayPlaylistDiagnostics.AddAuthoredEvidence(result.Path, result.Value, clpiByClip, diagnostics);
            var collapsed = BluRayPlaylistRules.CollapseRepeatedPlayItems(result.Value);
            if (collapsed.PlayItems.Count < result.Value.PlayItems.Count)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "info",
                    Code = "ODM_BD_PLAYLIST_LOOP",
                    Message = $"Playlist repeats play items; {result.Value.PlayItems.Count - collapsed.PlayItems.Count} repeated play item(s) are not emitted.",
                    Path = result.Path,
                });
            }

            // A playlist that holds a still frame indefinitely waits on the viewer, like a
            // menu, and MakeMKV still lists each of its clips as a stream title: on Monsters
            // University (UHD) 00002.mpls (stills 150, 151) and 00015.mpls (150, then still
            // 57) are listed, and so are 00150.m2ts, 00151.m2ts and 00057.m2ts. Clips of
            // looping playlists without stills are not: 00020.mpls plays 40 then 41 on
            // repeat and neither 00040.m2ts nor 00041.m2ts is listed. A still gallery cut
            // from one clip is that clip's playlist, and the clip is not listed again: on the
            // Monsters University bonus disc 00100.mpls holds 366 stills of clip 914, and
            // 00914.m2ts is not listed.
            if (collapsed.PlayItems.Any(item => item.StillMode == BluRayPlaylistRules.InfiniteStillMode)
                && collapsed.PlayItems.Select(item => item.ClipId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                foreach (var item in collapsed.PlayItems)
                {
                    stillPlaylistClips.Add(item.ClipId);
                }
            }

            var playlist = BluRayPlaylistRules.TrimTrailingNarrowerPlayItems(collapsed);
            if (playlist.PlayItems.Count < collapsed.PlayItems.Count)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "info",
                    Code = "ODM_BD_PLAYLIST_NARROW_TAIL",
                    Message = $"Playlist ends with {collapsed.PlayItems.Count - playlist.PlayItems.Count} play item(s) selecting fewer streams than its first play item; they are not emitted.",
                    Path = result.Path,
                });
            }

            if (playlist.SubPaths.Any(subPath => subPath.Type == DolbyVisionEnhancementLayerSubPathType))
            {
                foreach (var item in playlist.PlayItems)
                {
                    dolbyVisionClips.Add(item.ClipId);
                }
            }

            // Clips a playlist only plays alongside its main path (popup menus, secondary
            // audio or video) are not playback candidates of their own; MakeMKV never lists them.
            foreach (var subPath in playlist.SubPaths.Where(item => item.Type != DolbyVisionEnhancementLayerSubPathType))
            {
                foreach (var subPlayItem in subPath.SubPlayItems)
                {
                    subPathClips.Add(subPlayItem.ClipId);
                }
            }

            var evidencePaths = playlist.PlayItems.Select(item => $"BDMV/CLIPINF/{item.ClipId}.clpi")
                .Append(result.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool incompleteEvidence = diagnostics.Any(diagnostic => diagnostic.Severity is "warning" or "error"
                && diagnostic.Path is not null
                && evidencePaths.Contains(diagnostic.Path.Replace("/BACKUP/", "/", StringComparison.OrdinalIgnoreCase)));
            var parts = BluRayPlaylistRules.SplitIncompatiblePlayItems(
                playlist, clpiByClip, diagnostics, result.Path, incompleteEvidence);
            if (parts.Count > 1)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "info",
                    Code = "ODM_BD_PLAYLIST_SPLIT",
                    Message = $"Playlist is emitted as {parts.Count} parts because primary stream formats change at non-seamless play-item boundaries.",
                    Path = result.Path,
                });
            }

            for (int part = 0; part < parts.Count; part++)
            {
                titles.Add(BluRayTitleMapper.CreateBluRayTitle(
                    result.Path, parts[part], files, clpiByClip, diagnostics, parts.Count > 1 ? part : null));
            }
        }

        return new PlaylistCandidates(
            titles, isUhd, playlistCandidates, parsedPlaylists,
            dolbyVisionClips, subPathClips, stillPlaylistClips);
    }

    internal static async Task<BluRayParseResult> ParseBluRayAsync(
        DiscFileCatalog files,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        var clips = await ReadClipEvidenceAsync(files, diagnostics, metrics, cancellationToken);
        var playlists = await BuildPlaylistCandidatesAsync(files, clips.ByClip, diagnostics, metrics, cancellationToken);

        // MakeMKV walks playlists in on-disc directory order, so that order decides which of
        // two equivalent playlists survives and the sequence titles are listed in.
        var titles = playlists.Titles.OrderBy(title => files.EnumerationIndexOf(title.Source?.Path)).ToList();
        titles = BluRayTitleReconciler.ReconcileBluRayTitles(
            titles, files, clips.ByClip, playlists.DolbyVisionClips,
            playlists.SubPathClips, playlists.StillPlaylistClips, diagnostics);
        titles = BluRayTitleReconciler.OrderBluRayTitles(titles, files.EnumerationIndexOf);

        string? discName = await ReadBluRayDiscNameAsync(files, diagnostics, metrics, cancellationToken);
        if (playlists.CandidateCount == 0)
        {
            diagnostics.Add(ControlFileReader.MissingFile("BDMV/PLAYLIST/*.mpls"));
        }
        else if (playlists.ParsedCount != playlists.CandidateCount)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "warning",
                Code = "ODM_PLAYLISTS_PARTIAL",
                Message = $"Parsed {playlists.ParsedCount} of {playlists.CandidateCount} playlist files.",
            });
        }

        if (titles.Count > 0)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_TITLES_PARTIAL",
                Message = "Blu-ray playlist files are emitted as playback candidates; HDMV/BD-J logical-title resolution is not complete.",
            });
        }

        var manifestClips = CreateBluRayClips(files, clips.ByClip, clips.PathByClip);

        return new BluRayParseResult(titles, clips.IsUhd || playlists.IsUhd, manifestClips, discName);
    }

    private static async Task<string?> ReadBluRayDiscNameAsync(
        DiscFileCatalog files,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        const string primaryPath = "BDMV/META/DL/bdmv_dl.xml";
        const string backupPath = "BDMV/BACKUP/META/DL/bdmv_dl.xml";
        var primary = DiscFileCatalog.Find(files, primaryPath);
        var backup = DiscFileCatalog.Find(files, backupPath);

        foreach (var file in new[] { primary, backup }.OfType<NormalizedFile>())
        {
            var bytes = await ControlFileReader.ReadControlFileAsync(file, diagnostics, metrics, cancellationToken);
            if (bytes is null)
            {
                continue;
            }

            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = OpticalDiscManifestGenerator.MaxControlFileSize,
                });
                var document = XDocument.Load(reader);
                string? name = document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "title")
                    .SelectMany(element => element.Elements()
                        .Where(child => child.Name.LocalName == "name"))
                    .Select(element => element.Value.Trim())
                    .FirstOrDefault(value => value.Length > 0);
                if (name is not null)
                {
                    if (file == backup)
                    {
                        diagnostics.Add(new ManifestDiagnostic
                        {
                            Severity = "info",
                            Code = "ODM_CONTROL_FILE_BACKUP_USED",
                            Message = "Used backup disc metadata because BDMV/META/DL/bdmv_dl.xml was missing or did not contain a disc name.",
                            Path = backupPath,
                        });
                    }

                    return name;
                }
            }
            catch (XmlException ex)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "warning",
                    Code = "ODM_DISC_NAME_XML_INVALID",
                    Message = ex.Message,
                    Path = file.Path,
                });
            }
        }

        return null;
    }

    /// <summary>
    /// Builds CLPI-backed standalone clip candidates by pairing every observed
    /// <c>BDMV/STREAM/xxxxx.m2ts</c> stream file and/or <c>BDMV/CLIPINF/xxxxx.clpi</c>
    /// clip-information file by shared five-character stem. A clip is semantically
    /// distinct from a title: it never asserts playability and never reads M2TS/SSIF
    /// payload bytes, only file-size metadata and CLPI control-file evidence.
    /// </summary>
    private static IReadOnlyList<ManifestClip> CreateBluRayClips(
        IReadOnlyList<NormalizedFile> files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        IReadOnlyDictionary<string, string> clpiPathByClip)
    {
        var streamFilesByClip = files
            .Where(item => DiscFileCatalog.IsControlFile(item.Path, "BDMV/STREAM", ".m2ts"))
            .ToDictionary(item => Path.GetFileNameWithoutExtension(item.Path), StringComparer.OrdinalIgnoreCase);

        var clipIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        clipIds.UnionWith(streamFilesByClip.Keys);
        clipIds.UnionWith(clpiByClip.Keys);

        var clips = new List<ManifestClip>(clipIds.Count);
        foreach (var clipId in clipIds)
        {
            streamFilesByClip.TryGetValue(clipId, out var streamFile);
            clpiByClip.TryGetValue(clipId, out var clpi);
            clpiPathByClip.TryGetValue(clipId, out var clpiPath);

            clips.Add(new ManifestClip
            {
                ClipId = clipId,
                StreamPath = streamFile?.Path,
                ClipInfoPath = clpiPath,
                DurationSeconds = clpi?.PresentationSummary is not null
                    ? ManifestTiming.TicksToSeconds(clpi.PresentationSummary.DurationTicks45k)
                    : null,
                NumberOfSourcePackets = clpi?.ClipInfo.NumberOfSourcePackets,
                TransportStreamRecordingRate = clpi?.ClipInfo.TransportStreamRecordingRate,
                Streams = clpi is not null ? ManifestStreamMapper.CreateClpiStreams(clpi) : null,
            });
        }

        return clips;
    }

    private static IReadOnlyList<ControlCandidate> GetBluRayControlCandidates(
        IReadOnlyList<NormalizedFile> files,
        string primaryDirectory,
        string backupDirectory,
        string extension)
    {
        return files
            .Where(item => DiscFileCatalog.IsControlFile(item.Path, primaryDirectory, extension)
                || DiscFileCatalog.IsControlFile(item.Path, backupDirectory, extension))
            .Select(item => Path.GetFileName(item.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Select(name => new ControlCandidate(
                $"{primaryDirectory}/{name}",
                $"{backupDirectory}/{name}"))
            .ToArray();
    }

    private sealed record ControlCandidate(string PrimaryPath, string BackupPath);

    private sealed record ClipEvidence(
        bool IsUhd,
        IReadOnlyDictionary<string, ClpiFile> ByClip,
        IReadOnlyDictionary<string, string> PathByClip);

    private sealed record PlaylistCandidates(
        IReadOnlyList<ManifestTitle> Titles,
        bool IsUhd,
        int CandidateCount,
        int ParsedCount,
        IReadOnlySet<string> DolbyVisionClips,
        IReadOnlySet<string> SubPathClips,
        IReadOnlySet<string> StillPlaylistClips);

    internal sealed record BluRayParseResult(
        IReadOnlyList<ManifestTitle> Titles,
        bool IsUhd,
        IReadOnlyList<ManifestClip> Clips,
        string? DiscName);

    /// <summary>
    /// The MPLS subpath type of a Dolby Vision enhancement layer. MakeMKV also lists every
    /// clip that a playlist with such a subpath plays as a stream title. On Spartacus (UHD)
    /// this rule, with no playlist splitting, reproduces all 136 MakeMKV 1.18.3 titles. An
    /// older MakeMKV 1.16.4 log of 1917 does not list such clips.
    /// </summary>
    private const int DolbyVisionEnhancementLayerSubPathType = 10;
}
