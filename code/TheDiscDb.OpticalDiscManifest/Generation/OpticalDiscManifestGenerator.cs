using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscManifest.Serialization;
using TheDiscDb.OpticalDiscManifest.Validation;
using TheDiscDb.OpticalDiscParsers.Bdmv;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Dvd;
using TheDiscDb.OpticalDiscParsers.Dvd.Models;
using TheDiscDb.OpticalDiscParsers.Input;
using TheDiscDb.OpticalDiscParsers.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

public sealed partial class OpticalDiscManifestGenerator
{
    public const long MaxControlFileSize = 16 * 1024 * 1024;

    public const string SchemaUri =
        "https://schemas.thediscdb.com/optical-disc-manifest/v1/optical-disc-manifest.schema.json";

    private readonly OpticalDiscManifestSchemaValidator validator;

    public OpticalDiscManifestGenerator(OpticalDiscManifestSchemaValidator? validator = null)
    {
        this.validator = validator ?? new OpticalDiscManifestSchemaValidator();
    }

    public async Task<ManifestGenerationResult> GenerateAsync(
        ManifestGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        long memoryBefore = GC.GetTotalMemory(false);
        var diagnostics = new List<ManifestDiagnostic>();
        request.ReportProgress?.Invoke("Normalizing file inventory");
        var files = NormalizeFiles(request.Files, diagnostics);
        var readMetrics = new ReadMetrics();
        var format = DetectFormat(files);
        IReadOnlyList<ManifestTitle> titles = Array.Empty<ManifestTitle>();
        IReadOnlyList<ManifestClip> clips = Array.Empty<ManifestClip>();

        if (format == "dvd")
        {
            request.ReportProgress?.Invoke("Parsing DVD navigation metadata");
            titles = await ParseDvdAsync(files, diagnostics, readMetrics, cancellationToken);
        }
        else if (format == "blu-ray")
        {
            request.ReportProgress?.Invoke("Parsing Blu-ray navigation metadata");
            var bluRay = await ParseBluRayAsync(files, diagnostics, readMetrics, cancellationToken);
            format = bluRay.IsUhd ? "uhd-blu-ray" : "blu-ray";
            titles = bluRay.Titles;
            clips = bluRay.Clips;
        }
        else
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "error",
                Code = "ODM_FORMAT_UNKNOWN",
                Message = "No VIDEO_TS or BDMV control directory was found.",
            });
        }

        var capabilities = BuildCapabilities(format, request.Identifiers, titles, clips, diagnostics);
        request.ReportProgress?.Invoke("Building deterministic manifest");
        var manifest = new OpticalDiscManifestDocument
        {
            Schema = SchemaUri,
            SchemaVersion = 1,
            Producer = new ManifestProducer
            {
                Name = request.ProducerName,
                Version = request.ProducerVersion,
                Uri = request.ProducerUri,
            },
            Capabilities = capabilities,
            Disc = new ManifestDisc
            {
                Format = format,
                Identifiers = request.Identifiers?.Count > 0
                    ? request.Identifiers.OrderBy(item => item.Kind, StringComparer.Ordinal).ToArray()
                    : null,
                Files = files.Select(item => new ManifestFile
                {
                    Path = item.Path,
                    SizeBytes = item.File.Size,
                    Role = ClassifyRole(item.Path),
                }).ToArray(),
                Titles = titles.Count > 0 ? titles : null,
                Clips = clips.Count > 0 ? clips : null,
            },
            Diagnostics = diagnostics.Count > 0
                ? diagnostics
                    .OrderBy(item => item.Path, StringComparer.Ordinal)
                    .ThenBy(item => item.ByteOffset)
                    .ThenBy(item => item.Code, StringComparer.Ordinal)
                    .ToArray()
                : null,
        };

        var json = OpticalDiscManifestJson.Serialize(manifest);
        request.ReportProgress?.Invoke("Validating manifest");
        var validation = validator.Validate(json);
        stopwatch.Stop();

        return new ManifestGenerationResult
        {
            Manifest = manifest,
            Json = json,
            Validation = validation,
            Metrics = new ManifestScanMetrics
            {
                FilesEnumerated = files.Count,
                ControlFilesRead = readMetrics.FilesRead,
                ControlBytesRead = readMetrics.BytesRead,
                OutputBytes = json.LongLength,
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                ManagedMemoryBeforeBytes = memoryBefore,
                ManagedMemoryAfterBytes = GC.GetTotalMemory(false),
            },
        };
    }

    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Invalid disc-relative path: {path}");
        }

        return string.Join('/', segments);
    }

    private static IReadOnlyList<NormalizedFile> NormalizeFiles(
        IReadOnlyList<IManifestDiscFile> source,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        var files = new List<NormalizedFile>(source.Count);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in source)
        {
            string path;
            try
            {
                path = NormalizePath(file.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "error",
                    Code = "ODM_PATH_INVALID",
                    Message = ex.Message,
                });
                continue;
            }

            if (!paths.Add(path))
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "error",
                    Code = "ODM_PATH_DUPLICATE",
                    Message = "The selected directory contains duplicate case-insensitive paths.",
                    Path = path,
                });
                continue;
            }

            files.Add(new NormalizedFile(path, file));
        }

        return files.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
    }

    private static string DetectFormat(IReadOnlyList<NormalizedFile> files)
    {
        bool dvd = files.Any(item =>
            item.Path.StartsWith("VIDEO_TS/", StringComparison.OrdinalIgnoreCase));
        bool bluRay = files.Any(item =>
            item.Path.StartsWith("BDMV/", StringComparison.OrdinalIgnoreCase));

        return (dvd, bluRay) switch
        {
            (true, false) => "dvd",
            (false, true) => "blu-ray",
            _ => "unknown",
        };
    }

    private static async Task<IReadOnlyList<ManifestTitle>> ParseDvdAsync(
        IReadOnlyList<NormalizedFile> files,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        VmgiHeader? vmgi = null;
        var videoManagerResult = await ParseControlWithBackupAsync(
            files,
            "VIDEO_TS/VIDEO_TS.IFO",
            "VIDEO_TS/VIDEO_TS.BUP",
            diagnostics,
            metrics,
            cancellationToken,
            bytes => new DvdIfoParser(CreateReader(bytes)).ParseVmgiAsync().AsTask());
        if (videoManagerResult is null)
        {
            diagnostics.Add(MissingFile("VIDEO_TS/VIDEO_TS.IFO"));
        }
        else
        {
            vmgi = videoManagerResult.Value;
        }

        var titleSets = new Dictionary<int, VtsiHeader>();
        foreach (var candidate in GetDvdTitleSetCandidates(files))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int titleSet = candidate.TitleSetNumber;
            var result = await ParseControlWithBackupAsync(
                files,
                candidate.PrimaryPath,
                candidate.BackupPath,
                diagnostics,
                metrics,
                cancellationToken,
                bytes => new DvdIfoParser(CreateReader(bytes)).ParseVtsiAsync(titleSet).AsTask());
            if (result?.Value is null)
            {
                continue;
            }

            titleSets[titleSet] = result.Value;
        }

        if (vmgi is null || vmgi.Titles.Count == 0)
        {
            diagnostics.Add(DvdPartial(
                "The VMGI logical-title table could not be read, so DVD titles cannot be joined to their title sets."));
            return Array.Empty<ManifestTitle>();
        }

        var titles = new List<ManifestTitle>(vmgi.Titles.Count);
        foreach (var title in vmgi.Titles.OrderBy(item => item.Number))
        {
            if (!titleSets.TryGetValue(title.TitleSetNumber, out var vtsi))
            {
                diagnostics.Add(DvdPartial(
                    $"Logical title {title.Number} references missing title set {title.TitleSetNumber}."));
                continue;
            }

            var titleMap = vtsi.TitlePartMaps.SingleOrDefault(
                item => item.TitleSetTitleNumber == title.TitleSetTitleNumber);
            if (titleMap is null)
            {
                diagnostics.Add(DvdPartial(
                    $"Logical title {title.Number} references missing VTS title {title.TitleSetTitleNumber} in title set {title.TitleSetNumber}."));
                continue;
            }

            bool completeParts = titleMap.Parts.Count == title.NumberOfPartsOfTitle;
            if (!completeParts)
            {
                diagnostics.Add(DvdPartial(
                    $"Logical title {title.Number} declares {title.NumberOfPartsOfTitle} parts but {titleMap.Parts.Count} were parsed."));
            }

            bool timingSupported = title.NumberOfAngles == 1
                && !title.PlaybackFlags.IsMultiOrRandomPgcTitle;
            if (!timingSupported)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "info",
                    Code = "ODM_DVD_TIMING_PARTIAL",
                    Message = title.NumberOfAngles > 1
                        ? $"Chapter timing for logical title {title.Number} is omitted because it has {title.NumberOfAngles} angles."
                        : $"Chapter timing for logical title {title.Number} is omitted because it uses multi/random PGC playback.",
                });
            }

            var chapters = new List<ManifestChapter>(titleMap.Parts.Count);
            double elapsedSeconds = 0;
            long sizeBytes = 0;
            bool sizeComplete = timingSupported;
            bool timingComplete = timingSupported;
            foreach (var part in titleMap.Parts.OrderBy(item => item.Number))
            {
                var pgc = vtsi.ProgramChains.SingleOrDefault(
                    item => item.Number == part.ProgramChainNumber);
                if (pgc is null || part.ProgramNumber < 1 || part.ProgramNumber > pgc.NumberOfPrograms)
                {
                    diagnostics.Add(DvdPartial(
                        $"Logical title {title.Number} part {part.Number} references unavailable PGC {part.ProgramChainNumber}, program {part.ProgramNumber}."));
                    timingComplete = false;
                    sizeComplete = false;
                    chapters.Add(new ManifestChapter { Index = part.Number - 1 });
                    continue;
                }

                if (sizeComplete && TryGetProgramSizeBytes(pgc, part.ProgramNumber, out long programBytes))
                {
                    sizeBytes += programBytes;
                }
                else
                {
                    sizeComplete = false;
                }

                if (!timingComplete || !TryGetProgramDurationSeconds(pgc, part.ProgramNumber, out double duration))
                {
                    timingComplete = false;
                    chapters.Add(new ManifestChapter { Index = part.Number - 1 });
                    continue;
                }

                chapters.Add(new ManifestChapter
                {
                    Index = part.Number - 1,
                    StartSeconds = RoundSeconds(elapsedSeconds),
                    DurationSeconds = RoundSeconds(duration),
                });
                elapsedSeconds += duration;
            }

            if (timingSupported && !timingComplete)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "warning",
                    Code = "ODM_DVD_TIMING_PARTIAL",
                    Message = $"Chapter timing for logical title {title.Number} is incomplete because one or more program/cell ranges could not be resolved.",
                });
                chapters = titleMap.Parts
                    .OrderBy(item => item.Number)
                    .Select(item => new ManifestChapter { Index = item.Number - 1 })
                    .ToList();
            }

            var streams = CreateDvdStreams(vtsi);
            titles.Add(new ManifestTitle
            {
                Index = title.Number - 1,
                Source = new ManifestTitleSource
                {
                    Kind = "dvd-title",
                    Title = title.Number,
                    TitleSet = title.TitleSetNumber,
                    TitleSetTitle = title.TitleSetTitleNumber,
                },
                DurationSeconds = timingComplete ? RoundSeconds(elapsedSeconds) : null,
                SizeBytes = sizeComplete && completeParts && titleMap.Parts.Count > 0 ? sizeBytes : null,
                ChapterCount = title.NumberOfPartsOfTitle,
                Chapters = chapters.Count > 0 ? chapters : null,
                Streams = streams.Count > 0 ? streams : null,
            });
        }

        if (titles.Count != vmgi.Titles.Count)
        {
            diagnostics.Add(DvdPartial(
                $"Only {titles.Count} of {vmgi.Titles.Count} VMGI logical titles were joined successfully."));
        }

        return titles;
    }

    private const long DvdSectorSizeBytes = 2048;

    private static bool TryGetProgramDurationSeconds(
        ProgramChain pgc,
        int programNumber,
        out double durationSeconds)
    {
        durationSeconds = 0;
        if (!TryGetProgramCells(pgc, programNumber, out var cells))
        {
            return false;
        }

        durationSeconds = cells.Sum(item => item.PlaybackTime.ToTimeSpan().TotalSeconds);
        return true;
    }

    /// <summary>
    /// Sums the authored VOBU sector ranges (C_PBIT first/last sector) of the program's cells.
    /// This is IFO-only evidence and matches MakeMKV title sizes exactly for sequential PGC titles
    /// (Reservoir Dogs 2002 SE disc 1: 17/17 titles; Fight Club 1999 DVD main feature).
    /// </summary>
    private static bool TryGetProgramSizeBytes(
        ProgramChain pgc,
        int programNumber,
        out long sizeBytes)
    {
        sizeBytes = 0;
        if (!TryGetProgramCells(pgc, programNumber, out var cells))
        {
            return false;
        }

        foreach (var cell in cells)
        {
            if (cell.LastSector < cell.FirstSector)
            {
                sizeBytes = 0;
                return false;
            }

            sizeBytes += ((long)cell.LastSector - cell.FirstSector + 1) * DvdSectorSizeBytes;
        }

        return true;
    }

    private static bool TryGetProgramCells(
        ProgramChain pgc,
        int programNumber,
        out IReadOnlyList<CellPlaybackInfo> cells)
    {
        cells = Array.Empty<CellPlaybackInfo>();
        if (pgc.ProgramMap.Count != pgc.NumberOfPrograms
            || pgc.CellPlayback.Count != pgc.NumberOfCells
            || pgc.CellPositions.Count != pgc.NumberOfCells)
        {
            return false;
        }

        var program = pgc.ProgramMap.SingleOrDefault(item => item.ProgramNumber == programNumber);
        if (program is null)
        {
            return false;
        }

        int firstCell = program.FirstCellNumber;
        int lastCellExclusive = programNumber < pgc.ProgramMap.Count
            ? pgc.ProgramMap[programNumber].FirstCellNumber
            : pgc.NumberOfCells + 1;
        if (firstCell < 1 || lastCellExclusive <= firstCell || lastCellExclusive > pgc.NumberOfCells + 1)
        {
            return false;
        }

        cells = pgc.CellPlayback
            .Skip(firstCell - 1)
            .Take(lastCellExclusive - firstCell)
            .ToList();
        return true;
    }

    private static ManifestDiagnostic DvdPartial(string message)
        => new()
        {
            Severity = "warning",
            Code = "ODM_DVD_PARTIAL",
            Message = message,
        };

    private static IReadOnlyList<ManifestStream> CreateDvdStreams(VtsiHeader vtsi)
    {
        var streams = new List<ManifestStream>();
        streams.AddRange(vtsi.VideoStreams.OrderBy(item => item.Index).Select(item => new ManifestStream
        {
            Index = streams.Count,
            Type = "video",
            Codec = item.Codec,
            Resolution = item.Resolution,
            AspectRatio = item.AspectRatio,
            Line21ClosedCaptionFields = GetLine21ClosedCaptionFields(item),
        }));
        foreach (var stream in vtsi.AudioStreams.OrderBy(item => item.Index))
        {
            streams.Add(new ManifestStream
            {
                Index = streams.Count,
                Type = "audio",
                Codec = stream.Codec,
                Language = NormalizeLanguage(stream.LanguageCode),
                AudioLayout = stream.Channels,
            });
        }

        foreach (var stream in vtsi.SubtitleStreams.OrderBy(item => item.Index))
        {
            streams.Add(new ManifestStream
            {
                Index = streams.Count,
                Type = "subtitle",
                Codec = stream.CodingMode,
                Language = NormalizeLanguage(stream.LanguageCode),
            });
        }

        return streams;
    }

    private static IReadOnlyList<int>? GetLine21ClosedCaptionFields(VideoStreamInfo video)
    {
        if (!video.HasLine21ClosedCaptionField1 && !video.HasLine21ClosedCaptionField2)
        {
            return null;
        }

        var fields = new List<int>(2);
        if (video.HasLine21ClosedCaptionField1)
        {
            fields.Add(1);
        }

        if (video.HasLine21ClosedCaptionField2)
        {
            fields.Add(2);
        }

        return fields;
    }

    private static async Task<BluRayParseResult> ParseBluRayAsync(
        IReadOnlyList<NormalizedFile> files,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        bool isUhd = false;
        var indexResult = await ParseControlWithBackupAsync(
            files,
            "BDMV/index.bdmv",
            "BDMV/BACKUP/index.bdmv",
            diagnostics,
            metrics,
            cancellationToken,
            bytes => new BdmvParser(CreateReader(bytes)).ParseIndexAsync().AsTask());
        if (indexResult is null)
        {
            diagnostics.Add(MissingFile("BDMV/index.bdmv"));
        }
        else
        {
            isUhd |= indexResult.Value?.Version == "0300";
        }

        var movieObjectResult = await ParseControlWithBackupAsync(
            files,
            "BDMV/MovieObject.bdmv",
            "BDMV/BACKUP/MovieObject.bdmv",
            diagnostics,
            metrics,
            cancellationToken,
            bytes => new BdmvParser(CreateReader(bytes)).ParseMovieObjectsAsync().AsTask());
        if (movieObjectResult?.Value is not null)
        {
            isUhd |= movieObjectResult.Value.Version == "0300";
        }

        var clpiByClip = new Dictionary<string, ClpiFile>(StringComparer.OrdinalIgnoreCase);
        var clpiPathByClip = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GetBluRayControlCandidates(files, "BDMV/CLIPINF", "BDMV/BACKUP/CLIPINF", ".clpi"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ParseControlWithBackupAsync(
                files,
                candidate.PrimaryPath,
                candidate.BackupPath,
                diagnostics,
                metrics,
                cancellationToken,
                bytes => new ClpiParser(CreateReader(bytes)).ParseAsync().AsTask());
            if (result?.Value is not null)
            {
                var clipId = Path.GetFileNameWithoutExtension(result.Path);
                clpiByClip[clipId] = result.Value;
                clpiPathByClip[clipId] = result.Path;
                isUhd |= result.Value.Version == "0300";
            }
        }

        var titles = new List<ManifestTitle>();
        int playlistCandidates = 0;
        int parsedPlaylists = 0;
        foreach (var candidate in GetBluRayControlCandidates(files, "BDMV/PLAYLIST", "BDMV/BACKUP/PLAYLIST", ".mpls"))
        {
            playlistCandidates++;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ParseControlWithBackupAsync(
                files,
                candidate.PrimaryPath,
                candidate.BackupPath,
                diagnostics,
                metrics,
                cancellationToken,
                bytes => new MplsParser(CreateReader(bytes)).ParseAsync().AsTask());
            if (result?.Value is null)
            {
                continue;
            }

            parsedPlaylists++;
            isUhd |= result.Value.Version == "0300";
            titles.Add(CreateBluRayTitle(titles.Count, result.Path, result.Value, files, clpiByClip, diagnostics));
        }

        if (playlistCandidates == 0)
        {
            diagnostics.Add(MissingFile("BDMV/PLAYLIST/*.mpls"));
        }
        else if (parsedPlaylists != playlistCandidates)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "warning",
                Code = "ODM_PLAYLISTS_PARTIAL",
                Message = $"Parsed {parsedPlaylists} of {playlistCandidates} playlist files.",
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

        var clips = CreateBluRayClips(files, clpiByClip, clpiPathByClip);

        return new BluRayParseResult(titles, isUhd, clips);
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
            .Where(item => IsControlFile(item.Path, "BDMV/STREAM", ".m2ts"))
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
                SizeBytes = streamFile?.File.Size,
                ClipInfoPath = clpiPath,
                DurationSeconds = clpi?.PresentationSummary is not null
                    ? TicksToSeconds(clpi.PresentationSummary.DurationTicks45k)
                    : null,
                DurationTicks45k = clpi?.PresentationSummary?.DurationTicks45k,
                NumberOfSourcePackets = clpi?.ClipInfo.NumberOfSourcePackets,
                TransportStreamRecordingRate = clpi?.ClipInfo.TransportStreamRecordingRate,
                Streams = clpi is not null ? CreateClpiStreams(clpi) : null,
            });
        }

        return clips;
    }

    private static ManifestTitle CreateBluRayTitle(
        int index,
        string path,
        MplsPlaylist playlist,
        IReadOnlyList<NormalizedFile> files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        long cumulativeTicks = 0;
        var segments = new List<ManifestSegment>(playlist.PlayItems.Count);
        foreach (var item in playlist.PlayItems.OrderBy(item => item.Index))
        {
            long durationTicks = item.OutTime >= item.InTime ? item.OutTime - item.InTime : 0;
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
                var streamPath = $"BDMV/STREAM/{clip.value.ClipId}.m2ts";
                segments.Add(new ManifestSegment
                {
                    Index = segments.Count,
                    Clip = clip.value.ClipId,
                    FilePath = Find(files, streamPath)?.Path,
                    StartTicks45k = cumulativeTicks,
                    StartSeconds = TicksToSeconds(cumulativeTicks),
                    DurationTicks45k = durationTicks,
                    DurationSeconds = TicksToSeconds(durationTicks),
                    Angle = clips.Count > 1 ? clip.angleIndex + 1 : null,
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

        var chapters = CreateBluRayChapters(playlist, diagnostics, path);
        var streams = CreateBluRayStreams(playlist, clpiByClip);
        var stereoscopic3D = CreateStereoscopicView(playlist, diagnostics, path);
        AddUnsupportedExtensionDiagnostics(playlist, diagnostics, path);
        return new ManifestTitle
        {
            Index = index,
            Source = new ManifestTitleSource
            {
                Kind = "blu-ray-playlist",
                Path = path,
            },
            DurationTicks45k = cumulativeTicks,
            DurationSeconds = TicksToSeconds(cumulativeTicks),
            SizeBytes = ComputeTitleSizeBytes(segments, stereoscopic3D, files),
            ChapterCount = chapters.Count,
            Chapters = chapters.Count > 0 ? chapters : null,
            Segments = segments,
            Streams = streams.Count > 0 ? streams : null,
            Stereoscopic3D = stereoscopic3D,
        };
    }

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
        IReadOnlyList<NormalizedFile> files)
    {
        var clipIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in segments)
        {
            clipIds.Add(segment.Clip);
        }

        if (stereoscopic3D is not null)
        {
            clipIds.Add(stereoscopic3D.DependentClipId);
        }

        long? total = null;
        foreach (var clipId in clipIds)
        {
            var file = Find(files, $"BDMV/STREAM/{clipId}.m2ts");
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
        string path)
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

        long totalDurationTicks45k = cumulative;

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
                Index = chapters.Count,
                StartTicks45k = startTicks,
                StartSeconds = TicksToSeconds(startTicks),
                DurationTicks45k = mark.Duration,
                DurationSeconds = TicksToSeconds(mark.Duration),
            });
        }

        return chapters;
    }

    private static IReadOnlyList<ManifestStream> CreateBluRayStreams(
        MplsPlaylist playlist,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip)
    {
        var candidates = new List<MplsStream>();
        foreach (var item in playlist.PlayItems.OrderBy(item => item.Index))
        {
            candidates.AddRange(item.StreamTable.VideoStreams);
            candidates.AddRange(item.StreamTable.AudioStreams);
            candidates.AddRange(item.StreamTable.PresentationGraphicsStreams);
            candidates.AddRange(item.StreamTable.InteractiveGraphicsStreams);
            candidates.AddRange(item.StreamTable.SecondaryAudioStreams);
            candidates.AddRange(item.StreamTable.SecondaryVideoStreams);
        }

        var streams = new List<ManifestStream>();
        foreach (var stream in candidates
            .DistinctBy(item => (item.Category, item.Pid, item.CodingTypeCode, item.LanguageCode))
            .OrderBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Pid)
            .ThenBy(item => item.CodingTypeCode))
        {
            var clpi = FindClpiStream(playlist, clpiByClip, stream.Pid);
            streams.Add(new ManifestStream
            {
                Index = streams.Count,
                Type = MapStreamType(stream.Category),
                Codec = clpi?.CodingType ?? stream.CodingType,
                Pid = stream.Pid,
                CodingTypeCode = clpi?.CodingTypeCode ?? stream.CodingTypeCode,
                FormatCode = clpi?.FormatCode ?? stream.FormatCode,
                RateCode = clpi?.RateCode ?? stream.RateCode,
                DynamicRangeTypeCode = clpi?.DynamicRangeTypeCode,
                ColorSpaceCode = clpi?.ColorSpaceCode,
                HdrPlusFlag = clpi?.HdrPlusFlag,
                Language = NormalizeLanguage(clpi?.LanguageCode ?? stream.LanguageCode),
            });
        }

        return streams;
    }

    private static ClpiProgramStream? FindClpiStream(
        MplsPlaylist playlist,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        ushort? pid)
    {
        if (pid is null)
        {
            return null;
        }

        foreach (var clipId in playlist.PlayItems
            .SelectMany(item => item.Clips.Count > 0
                ? item.Clips.Select(clip => clip.ClipId)
                : new[] { item.ClipId })
            .Distinct(StringComparer.Ordinal))
        {
            if (clpiByClip.TryGetValue(clipId, out var clpi))
            {
                var stream = clpi.Programs
                    .SelectMany(program => program.Streams)
                    .FirstOrDefault(item => item.Pid == pid);
                if (stream is not null)
                {
                    return stream;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maps CLPI stream evidence (ProgramInfo streams plus any ExtensionData streams,
    /// such as the MVC dependent-view stream declared for 3D dependent clips) into
    /// deterministic <see cref="ManifestStream"/> records for a standalone clip
    /// candidate. This is CLPI-only evidence: it does not cross-reference any MPLS
    /// playlist stream table.
    /// </summary>
    private static IReadOnlyList<ManifestStream> CreateClpiStreams(ClpiFile clpi)
    {
        var candidates = clpi.Programs.SelectMany(program => program.Streams)
            .Concat(clpi.ExtensionStreams);

        var streams = new List<ManifestStream>();
        foreach (var stream in candidates
            .DistinctBy(item => (item.Category, item.Pid, item.CodingTypeCode, item.LanguageCode))
            .OrderBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Pid)
            .ThenBy(item => item.CodingTypeCode))
        {
            streams.Add(new ManifestStream
            {
                Index = streams.Count,
                Type = MapStreamType(stream.Category),
                Codec = stream.CodingType,
                Pid = stream.Pid,
                CodingTypeCode = stream.CodingTypeCode,
                FormatCode = stream.FormatCode,
                RateCode = stream.RateCode,
                DynamicRangeTypeCode = stream.DynamicRangeTypeCode,
                ColorSpaceCode = stream.ColorSpaceCode,
                HdrPlusFlag = stream.HdrPlusFlag,
                Language = NormalizeLanguage(stream.LanguageCode),
            });
        }

        return streams;
    }

    private static IReadOnlyList<string> BuildCapabilities(
        string format,
        IReadOnlyList<ManifestIdentifier>? identifiers,
        IReadOnlyList<ManifestTitle> titles,
        IReadOnlyList<ManifestClip> clips,
        IReadOnlyList<ManifestDiagnostic> diagnostics)
    {
        var capabilities = new List<string>();
        if (identifiers?.Count > 0)
        {
            capabilities.Add("disc.identifiers");
            capabilities.Add("disc.files.hash-inputs");
        }

        capabilities.Add("disc.files.complete");
        if (titles.Count > 0)
        {
            capabilities.Add("disc.titles");
        }

        bool hasErrors = diagnostics.Any(item => item.Severity == "error");
        bool partialDvd = diagnostics.Any(item => item.Code == "ODM_DVD_PARTIAL");
        bool completeLogicalTitlesSupported = format == "dvd";
        if (completeLogicalTitlesSupported && titles.Count > 0 && !hasErrors && !partialDvd)
        {
            capabilities.Add("disc.titles.complete");
        }

        if (titles.Any(item => item.Segments?.Count > 0))
        {
            capabilities.Add("disc.segments");
        }

        if (titles.Any(item => item.Streams?.Count > 0))
        {
            capabilities.Add("disc.streams.declared");
        }

        if (titles.Any(item => item.ChapterCount is not null))
        {
            capabilities.Add("disc.chapters.counts");
        }

        if (titles.Any(item => item.Chapters?.Any(chapter => chapter.StartSeconds is not null) == true))
        {
            capabilities.Add("disc.chapters.timing");
        }

        if (titles.Any(item => item.Stereoscopic3D is not null))
        {
            capabilities.Add("disc.titles.stereoscopic-3d");
        }

        if (clips.Count > 0)
        {
            capabilities.Add("disc.clips");
        }

        return capabilities;
    }

    private static async Task<ParsedControl<T>?> ParseControlWithBackupAsync<T>(
        IReadOnlyList<NormalizedFile> files,
        string primaryPath,
        string backupPath,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken,
        Func<byte[], Task<ParserResult<T>>> parseAsync)
        where T : class
    {
        var primary = Find(files, primaryPath);
        var backup = Find(files, backupPath);
        var parsed = primary is not null
            ? await TryParseControlAsync(primary, diagnostics, metrics, cancellationToken, parseAsync)
            : null;
        if (parsed?.Result.IsSuccessful == true || backup is null)
        {
            return parsed;
        }

        var backupParsed = await TryParseControlAsync(backup, diagnostics, metrics, cancellationToken, parseAsync);
        if (backupParsed is not null)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_CONTROL_FILE_BACKUP_USED",
                Message = primary is null
                    ? $"Used backup control file because {primaryPath} was missing."
                    : $"Used backup control file because {primaryPath} could not be parsed completely.",
                Path = backup.Path,
            });
        }

        return backupParsed ?? parsed;
    }

    private static async Task<ParsedControl<T>?> TryParseControlAsync<T>(
        NormalizedFile file,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken,
        Func<byte[], Task<ParserResult<T>>> parseAsync)
        where T : class
    {
        var bytes = await ReadControlFileAsync(file, diagnostics, metrics, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        var result = await parseAsync(bytes);
        AddDiagnostics(diagnostics, file.Path, result.Diagnostics);
        return new ParsedControl<T>(file.Path, result.Value, result);
    }

    private static IReadOnlyList<DvdTitleSetCandidate> GetDvdTitleSetCandidates(IReadOnlyList<NormalizedFile> files)
    {
        var titleSets = new SortedSet<int>();
        foreach (var file in files)
        {
            var match = VtsControlPattern().Match(file.Path);
            if (match.Success)
            {
                titleSets.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        return titleSets
            .Select(number => new DvdTitleSetCandidate(
                number,
                $"VIDEO_TS/VTS_{number:00}_0.IFO",
                $"VIDEO_TS/VTS_{number:00}_0.BUP"))
            .ToArray();
    }

    private static IReadOnlyList<ControlCandidate> GetBluRayControlCandidates(
        IReadOnlyList<NormalizedFile> files,
        string primaryDirectory,
        string backupDirectory,
        string extension)
    {
        return files
            .Where(item => IsControlFile(item.Path, primaryDirectory, extension)
                || IsControlFile(item.Path, backupDirectory, extension))
            .Select(item => Path.GetFileName(item.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Select(name => new ControlCandidate(
                $"{primaryDirectory}/{name}",
                $"{backupDirectory}/{name}"))
            .ToArray();
    }

    private static async Task<byte[]?> ReadControlFileAsync(
        NormalizedFile file,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (file.File.Size < 0 || file.File.Size > MaxControlFileSize)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "error",
                Code = "ODM_CONTROL_FILE_SIZE",
                Message = $"Control file size {file.File.Size} exceeds the {MaxControlFileSize}-byte limit.",
                Path = file.Path,
            });
            return null;
        }

        try
        {
            var bytes = await file.File.ReadBytesAsync(MaxControlFileSize, cancellationToken);
            metrics.FilesRead++;
            metrics.BytesRead += bytes.LongLength;
            if (bytes.LongLength != file.File.Size)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "warning",
                    Code = "ODM_CONTROL_FILE_TRUNCATED",
                    Message = $"Expected {file.File.Size} bytes but read {bytes.LongLength}.",
                    Path = file.Path,
                });
            }

            return bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "error",
                Code = "ODM_CONTROL_FILE_READ",
                Message = ex.Message,
                Path = file.Path,
            });
            return null;
        }
    }

    private static void AddDiagnostics(
        ICollection<ManifestDiagnostic> destination,
        string path,
        IEnumerable<ParserDiagnostic> source)
    {
        foreach (var diagnostic in source)
        {
            destination.Add(new ManifestDiagnostic
            {
                Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                Code = diagnostic.Code,
                Message = diagnostic.Message,
                Path = path,
                ByteOffset = diagnostic.Offset,
            });
        }
    }

    private static OpticalDiscBinaryReader CreateReader(byte[] bytes)
        => new(new MemoryOpticalDiscReader(bytes));

    private static NormalizedFile? Find(IEnumerable<NormalizedFile> files, string path)
        => files.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

    private static bool IsControlFile(string path, string directory, string extension)
        => path.StartsWith($"{directory}/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            && path[(directory.Length + 1)..].IndexOf('/') < 0;

    private static ManifestDiagnostic MissingFile(string path)
        => new()
        {
            Severity = "warning",
            Code = "ODM_CONTROL_FILE_MISSING",
            Message = "Expected control metadata was not found.",
            Path = path,
        };

    private static string ClassifyRole(string path)
    {
        if (path.Contains("/BACKUP/", StringComparison.OrdinalIgnoreCase))
        {
            return "backup";
        }

        if (path.EndsWith(".mpls", StringComparison.OrdinalIgnoreCase))
        {
            return "playlist";
        }

        if (path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".vob", StringComparison.OrdinalIgnoreCase))
        {
            return "stream";
        }

        if (path.EndsWith(".ifo", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".clpi", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".bdmv", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("AACS/", StringComparison.OrdinalIgnoreCase))
        {
            return "metadata";
        }

        return "other";
    }

    private static string MapStreamType(string category)
        => category switch
        {
            "Video" or "SecondaryVideo" => "video",
            "Audio" or "SecondaryAudio" => "audio",
            "PresentationGraphics" or "InteractiveGraphics" => "subtitle",
            _ => "unknown",
        };

    private static string? NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) || language == "und"
            ? null
            : language.Trim().ToLowerInvariant();

    private static double TicksToSeconds(long ticks)
        => RoundSeconds(ticks / 45_000d);

    private static double RoundSeconds(double seconds)
        => Math.Round(seconds, 6, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"^VIDEO_TS/VTS_(\d{2})_0\.(IFO|BUP)$", RegexOptions.IgnoreCase)]
    private static partial Regex VtsControlPattern();

    private sealed record NormalizedFile(string Path, IManifestDiscFile File);

    private sealed record DvdTitleSetCandidate(int TitleSetNumber, string PrimaryPath, string BackupPath);

    private sealed record ControlCandidate(string PrimaryPath, string BackupPath);

    private sealed record ParsedControl<T>(string Path, T? Value, ParserResult<T> Result) where T : class;

    private sealed record BluRayParseResult(IReadOnlyList<ManifestTitle> Titles, bool IsUhd, IReadOnlyList<ManifestClip> Clips);

    private sealed class ReadMetrics
    {
        public int FilesRead { get; set; }

        public long BytesRead { get; set; }
    }
}
