using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TheDiscDb.Core.DiscHash;
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
        string? discName = null;
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
            discName = bluRay.DiscName;
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
            Disc = new ManifestDisc
            {
                Format = format,
                Name = discName,
                Identifiers = CreateIdentifiers(files, format, request.Identifiers),
                Files = files.Select(item => new ManifestFile
                {
                    Path = item.Path,
                    SizeBytes = item.File.Size,
                }).ToArray(),
                Titles = titles.Count > 0 ? titles : null,
                Clips = clips.Count > 0 ? clips : null,
            },
        };

        var json = OpticalDiscManifestJson.Serialize(manifest);
        request.ReportProgress?.Invoke("Validating manifest");
        var validation = validator.Validate(json);
        stopwatch.Stop();

        return new ManifestGenerationResult
        {
            Manifest = manifest,
            Diagnostics = diagnostics
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ThenBy(item => item.ByteOffset)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ToArray(),
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

    private static IReadOnlyList<ManifestIdentifier> CreateIdentifiers(
        IReadOnlyList<NormalizedFile> files,
        string format,
        IReadOnlyList<ManifestIdentifier>? suppliedIdentifiers)
    {
        var identifiers = (suppliedIdentifiers ?? [])
            .Where(item => !string.Equals(item.Kind, "thediscdb-content-hash", StringComparison.Ordinal))
            .Append(new ManifestIdentifier
            {
                Kind = "thediscdb-content-hash",
                Value = ComputeContentHash(files, format),
            })
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ToArray();

        return identifiers;
    }

    private static string ComputeContentHash(IReadOnlyList<NormalizedFile> files, string format)
    {
        var payloadFiles = files
            .Where(item => IsContentHashInput(item.Path, format))
            .OrderBy(item => Path.GetFileName(item.Path), StringComparer.Ordinal);

        // System.Security.Cryptography.MD5 throws Cryptography_UnknownHashAlgorithm in
        // Blazor WebAssembly, where the crypto backend only exposes SubtleCrypto's SHA family.
        var hash = new Md5Digest();
        Span<byte> sizeBytes = stackalloc byte[sizeof(long)];
        foreach (var file in payloadFiles)
        {
            if (file.File.Size < 0)
            {
                throw new InvalidDataException($"File '{file.Path}' has a negative size and cannot be hashed.");
            }

            BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, file.File.Size);
            hash.Append(sizeBytes);
        }

        return Convert.ToHexString(hash.Finish());
    }

    private static bool IsContentHashInput(string path, string format)
    {
        if (path.StartsWith("BDMV/BACKUP/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ssif", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return format switch
        {
            "blu-ray" or "uhd-blu-ray" => IsControlFile(path, "BDMV/STREAM", ".m2ts"),
            "dvd" => path.StartsWith("VIDEO_TS/", StringComparison.OrdinalIgnoreCase)
                && (path.EndsWith(".vob", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".ifo", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".bup", StringComparison.OrdinalIgnoreCase)),
            _ => false,
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
        int skippedTitles = 0;
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
            var segments = new List<ManifestSegment>();
            double elapsedSeconds = 0;
            long sizeBytes = 0;
            bool sizeComplete = timingSupported;
            bool timingComplete = timingSupported;
            bool anyChapterMark = false;
            bool anyCells = false;
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
                    chapters.Add(new ManifestChapter { StartSeconds = 0 });
                    continue;
                }

                if (!TryGetProgramCells(pgc, part.ProgramNumber, out var programCells))
                {
                    timingComplete = false;
                    sizeComplete = false;
                    chapters.Add(new ManifestChapter { StartSeconds = 0 });
                    continue;
                }

                anyCells = true;
                var (firstPlayedCell, lastPlayedCell) = GetPlayedCellRange(pgc);
                var playedCells = programCells
                    .Where(cell => cell.Number >= firstPlayedCell && cell.Number <= lastPlayedCell)
                    .ToList();
                if (playedCells.Count == 0)
                {
                    continue;
                }

                if (sizeComplete && TryGetCellSizeBytes(playedCells, out long programBytes))
                {
                    sizeBytes += programBytes;
                }
                else
                {
                    sizeComplete = false;
                }

                if (!timingComplete)
                {
                    continue;
                }

                double duration = playedCells.Sum(cell => cell.PlaybackTime.ToTimeSpan().TotalSeconds);
                anyChapterMark |= playedCells[0].Number == programCells[0].Number;
                chapters.Add(new ManifestChapter
                {
                    StartSeconds = RoundSeconds(elapsedSeconds),
                    DurationSeconds = RoundSeconds(duration),
                });

                double cellStart = elapsedSeconds;
                foreach (var cell in playedCells)
                {
                    var position = pgc.CellPositions[cell.Number - 1];
                    double cellSeconds = cell.PlaybackTime.ToTimeSpan().TotalSeconds;
                    segments.Add(new ManifestSegment
                    {
                        Clip = FormatDvdCellClip(position),
                        StartSeconds = RoundSeconds(cellStart),
                        DurationSeconds = RoundSeconds(cellSeconds),
                        Cell = cell.Number,
                    });
                    cellStart += cellSeconds;
                }

                elapsedSeconds += duration;
            }

            if (timingSupported && !anyCells)
            {
                // MakeMKV does not list a title none of whose programs resolve to cells, such as
                // United 93's PGC 2 titles whose program map reads [1, 0].
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "info",
                    Code = "ODM_DVD_TITLE_SKIPPED",
                    Message = $"Logical title {title.Number} is omitted because none of its programs resolve to cells.",
                });
                skippedTitles++;
                continue;
            }

            // A chapter starts where a program does. When every played program had its first cells
            // skipped there is no chapter mark at all, and MakeMKV reports no chapters (United 93
            // title 3); otherwise the title start counts as one.
            if (!anyChapterMark)
            {
                chapters.Clear();
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
                    .Select(_ => new ManifestChapter { StartSeconds = 0 })
                    .ToList();
            }

            var streams = CreateDvdStreams(vtsi);
            titles.Add(new ManifestTitle
            {
                Source = new ManifestTitleSource
                {
                    Title = title.Number,
                    TitleSet = title.TitleSetNumber,
                    TitleSetTitle = title.TitleSetTitleNumber,
                },
                DurationSeconds = timingComplete ? RoundSeconds(elapsedSeconds) : null,
                SizeBytes = sizeComplete && completeParts && titleMap.Parts.Count > 0 ? sizeBytes : null,
                Chapters = chapters.Count > 0 && timingComplete ? chapters : null,
                Segments = segments.Count > 0 && timingComplete ? segments : null,
                Streams = streams.Count > 0 ? streams : null,
            });
        }

        if (titles.Count + skippedTitles != vmgi.Titles.Count)
        {
            diagnostics.Add(DvdPartial(
                $"Only {titles.Count} of {vmgi.Titles.Count} VMGI logical titles were joined successfully."));
        }

        return titles;
    }

    private const long DvdSectorSizeBytes = 2048;

    /// <summary>
    /// The longest duration that can still be a single video frame, taken from the
    /// slowest frame rate a BD-ROM stream may declare (23.976 fps, one frame every
    /// ~0.0417 seconds) with a small tolerance.
    /// </summary>
    private const double SingleFrameSeconds = 0.05;

    /// <summary>
    /// The MPLS subpath type of a Dolby Vision enhancement layer. MakeMKV also lists every
    /// clip that a playlist with such a subpath plays as a stream title. On Spartacus (UHD)
    /// this rule, with no playlist splitting, reproduces all 136 MakeMKV 1.18.3 titles. An
    /// older MakeMKV 1.16.4 log of 1917 does not list such clips.
    /// </summary>
    private const int DolbyVisionEnhancementLayerSubPathType = 10;

    /// <summary>
    /// Finds the cells of a PGC that sequential playback actually reaches, following the cell
    /// commands the way MakeMKV does when it reports "Cells 1-2 were removed from title start"
    /// and "Cells 26-26 were removed from title end". On United 93 the feature's cell 1 runs
    /// <c>LinkCN 3</c>, skipping a 3,324-sector cell that claims to play for half a second, and
    /// cell 25 runs <c>LinkTailPGC</c>, so cell 26 is never played. This reproduces all six
    /// MakeMKV titles of that disc.
    /// </summary>
    internal static (int First, int Last) GetPlayedCellRange(ProgramChain pgc)
    {
        int cellCount = pgc.CellPlayback.Count;
        int first = 1;
        while (first <= cellCount
            && TryGetCellCommand(pgc, first, out ulong command)
            && GetLinkedCell(command) is int target
            && target > first + 1
            && target <= cellCount)
        {
            first = target;
        }

        int last = cellCount;
        for (int cell = first; cell < cellCount; cell++)
        {
            if (TryGetCellCommand(pgc, cell, out ulong command) && LeavesProgramChain(command))
            {
                last = cell;
                break;
            }
        }

        return first <= cellCount ? (first, last) : (1, cellCount);
    }

    private static bool TryGetCellCommand(ProgramChain pgc, int cellNumber, out ulong command)
    {
        command = 0;
        int commandNumber = pgc.CellPlayback[cellNumber - 1].CellCommandNumber;
        if (commandNumber < 1 || commandNumber > pgc.CellCommands.Count)
        {
            return false;
        }

        command = pgc.CellCommands[commandNumber - 1];
        return true;
    }

    // Link commands have 0b0010 in their top nibble and the link kind in the low nibble of the
    // second byte (DVD-Video navigation command set, as implemented by libdvdnav).
    private static bool IsLinkCommand(ulong command) => (command >> 60) == 0x2;

    private static int LinkKind(ulong command) => (int)((command >> 48) & 0x0F);

    private static int? GetLinkedCell(ulong command)
    {
        const int LinkCN = 7;
        return IsLinkCommand(command) && LinkKind(command) == LinkCN ? (int)(command & 0xFF) : null;
    }

    private static bool LeavesProgramChain(ulong command)
    {
        const int LinkSIns = 1;
        const int LinkPGCN = 4;
        if ((command >> 60) == 0x3)
        {
            // Jump and call commands always leave the PGC.
            return true;
        }

        if (!IsLinkCommand(command))
        {
            return false;
        }

        int kind = LinkKind(command);
        if (kind == LinkPGCN)
        {
            return true;
        }

        // LinkSIns 0x09-0x0D are LinkTopPGC, LinkNextPGC, LinkPrevPGC, LinkGoUpPGC and
        // LinkTailPGC; each ends sequential playback of the PGC's cells.
        int instruction = (int)(command & 0x1F);
        return kind == LinkSIns && instruction is >= 0x09 and <= 0x0D;
    }

    /// <summary>
    /// Sums the authored VOBU sector ranges (C_PBIT first/last sector) of the cells. This is
    /// IFO-only evidence and matches MakeMKV title sizes exactly for sequential PGC titles
    /// (Reservoir Dogs 2002 SE disc 1: 17/17 titles; Fight Club 1999 DVD main feature; United 93,
    /// counting only the cells playback reaches).
    /// </summary>
    private static bool TryGetCellSizeBytes(IEnumerable<CellPlaybackInfo> cells, out long sizeBytes)
    {
        sizeBytes = 0;
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

    /// <summary>
    /// Names a DVD cell by its VOB ID and cell ID (C_POSIT), written as <c>"{vobId}.{cellId}"</c>.
    /// This is the cell's address on disc, the DVD counterpart of a Blu-ray clip.
    /// </summary>
    internal static string FormatDvdCellClip(CellPositionInfo position)
        => string.Create(CultureInfo.InvariantCulture, $"{position.VobId}.{position.CellId}");

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
            Type = "video",
            Category = "video",
            Codec = item.Codec,
            Resolution = item.Resolution,
            AspectRatio = item.AspectRatio,
            FrameRate = item.FrameRate,
            Line21ClosedCaptionFields = GetLine21ClosedCaptionFields(item),
        }));
        foreach (var stream in vtsi.AudioStreams.OrderBy(item => item.Index))
        {
            streams.Add(new ManifestStream
            {
                Type = "audio",
                Category = "audio",
                Codec = stream.Codec,
                Language = NormalizeLanguage(stream.LanguageCode),
                AudioLayout = stream.Channels,
            });
        }

        foreach (var stream in vtsi.SubtitleStreams.OrderBy(item => item.Index))
        {
            streams.Add(new ManifestStream
            {
                Type = "subtitle",
                Category = "subpicture",
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
        var dolbyVisionClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            var playlist = CollapseRepeatedPlayItems(result.Value);
            if (playlist.PlayItems.Count < result.Value.PlayItems.Count)
            {
                diagnostics.Add(new ManifestDiagnostic
                {
                    Severity = "info",
                    Code = "ODM_BD_PLAYLIST_LOOP",
                    Message = $"Playlist repeats play items back to back; {result.Value.PlayItems.Count - playlist.PlayItems.Count} repeated play item(s) are not emitted.",
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

            titles.Add(CreateBluRayTitle(result.Path, playlist, files, clpiByClip, diagnostics, null));
        }

        titles = ReconcileBluRayTitles(titles, files, clpiByClip, dolbyVisionClips, diagnostics);

        string? discName = await ReadBluRayDiscNameAsync(files, diagnostics, metrics, cancellationToken);
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

        return new BluRayParseResult(titles, isUhd, clips, discName);
    }

    private static async Task<string?> ReadBluRayDiscNameAsync(
        IReadOnlyList<NormalizedFile> files,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        const string primaryPath = "BDMV/META/DL/bdmv_dl.xml";
        const string backupPath = "BDMV/BACKUP/META/DL/bdmv_dl.xml";
        var primary = Find(files, primaryPath);
        var backup = Find(files, backupPath);

        foreach (var file in new[] { primary, backup }.OfType<NormalizedFile>())
        {
            var bytes = await ReadControlFileAsync(file, diagnostics, metrics, cancellationToken);
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
                    MaxCharactersInDocument = MaxControlFileSize,
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
    /// Aligns the playlist-derived title list with the set of playback candidates an
    /// optical disc actually offers, so a manifest produced from the disc describes the
    /// same titles a MakeMKV log of that disc would describe. Three reconciliations run,
    /// in order: a playlist that plays exactly like an earlier one and selects no stream
    /// it lacks collapses into it; stream files no retained playlist references, or that a Dolby Vision playlist plays,
    /// are promoted to titles of their own; and a title whose only content is a single unchaptered clip,
    /// played in full, is attributed to that clip's stream file, because the playlist adds
    /// nothing the stream does not already state. The result is then ordered with
    /// playlist-sourced titles ahead of stream-sourced ones. Each reconciliation is
    /// evidence-driven and never discards a distinct playback candidate.
    /// </summary>
    private static List<ManifestTitle> ReconcileBluRayTitles(
        IReadOnlyList<ManifestTitle> titles,
        IReadOnlyList<NormalizedFile> files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        IReadOnlySet<string> dolbyVisionClips,
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

            samePlayback.Add(title);
            retained.Add(title);
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in retained)
        {
            foreach (var segment in title.Segments ?? [])
            {
                referenced.Add(segment.Clip);
            }

            // A stereoscopic dependent view is carried by the title it pairs with rather
            // than by a segment, so it is already reachable and is not its own candidate.
            if (title.Stereoscopic3D is { } view)
            {
                referenced.Add(view.BaseClipId);
                referenced.Add(view.DependentClipId);
            }
        }

        var streamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(item => IsControlFile(item.Path, "BDMV/STREAM", ".m2ts")))
        {
            streamPaths.Add(file.Path);
        }

        var promoted = files
            .Where(item => IsControlFile(item.Path, "BDMV/STREAM", ".m2ts"))
            .Where(item => !referenced.Contains(Path.GetFileNameWithoutExtension(item.Path))
                || dolbyVisionClips.Contains(Path.GetFileNameWithoutExtension(item.Path)))
            .Where(item => IsPlayableStreamCandidate(
                GetClipDurationSeconds(Path.GetFileNameWithoutExtension(item.Path), clpiByClip)))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var file in promoted)
        {
            string clipId = Path.GetFileNameWithoutExtension(file.Path);
            retained.Add(CreateBluRayStreamTitle(file, clipId, GetClip(clipId, clpiByClip)));
        }

        if (promoted.Length > 0)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_BD_STREAM_TITLES",
                Message = $"Promoted {promoted.Length} stream file(s) to titles because no playlist references them or a Dolby Vision playlist plays them.",
            });
        }

        for (int index = 0; index < retained.Count; index++)
        {
            retained[index] = AttributeSingleClipTitleToStream(
                retained[index],
                streamPaths,
                clipId => GetClipDurationSeconds(clipId, clpiByClip));
        }

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

        return OrderBluRayTitles(retained);
    }

    /// <summary>
    /// Orders titles the way MakeMKV presents them, so contributors labelling a disc see
    /// the same sequence they know from MakeMKV. Across the TheDiscDb log corpus MakeMKV
    /// lists every playlist-sourced title before every stream-sourced title, without
    /// exception. Playlist titles keep their playlist order; stream titles follow in
    /// clip order. MakeMKV's order within each block depends on evidence a manifest does
    /// not carry, so it is not reproduced.
    /// </summary>
    internal static List<ManifestTitle> OrderBluRayTitles(IReadOnlyList<ManifestTitle> titles)
    {
        static bool IsStreamSourced(ManifestTitle title)
            => title.Source?.Path?.StartsWith("BDMV/STREAM/", StringComparison.OrdinalIgnoreCase) == true;

        return titles
            .Where(title => !IsStreamSourced(title))
            .Concat(titles
                .Where(IsStreamSourced)
                .OrderBy(title => title.Source!.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static ClpiFile? GetClip(string clipId, IReadOnlyDictionary<string, ClpiFile> clpiByClip)
        => clpiByClip.TryGetValue(clipId, out var clpi) ? clpi : null;

    private static double? GetClipDurationSeconds(
        string clipId,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip)
    {
        var summary = GetClip(clipId, clpiByClip)?.PresentationSummary;
        return summary is not null ? TicksToSeconds(summary.DurationTicks45k) : null;
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
                .Append(';');
        }

        builder
            .Append('|')
            .Append(title.Stereoscopic3D?.DependentClipId ?? "-")
            .Append('|');

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
    /// not nested.
    /// </summary>
    internal static bool SelectsNoStreamBeyond(ManifestTitle title, ManifestTitle earlier)
    {
        var available = new HashSet<string>((earlier.Streams ?? []).Select(CreateStreamKey), StringComparer.Ordinal);
        return (title.Streams ?? []).All(stream => available.Contains(CreateStreamKey(stream)));
    }

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
        Func<string, double?> clipDurationSeconds)
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
            ? TicksToSeconds(clpi.PresentationSummary.DurationTicks45k)
            : null;
        var streams = clpi is not null ? CreateClpiStreams(clpi) : [];
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
                ClipInfoPath = clpiPath,
                DurationSeconds = clpi?.PresentationSummary is not null
                    ? TicksToSeconds(clpi.PresentationSummary.DurationTicks45k)
                    : null,
                NumberOfSourcePackets = clpi?.ClipInfo.NumberOfSourcePackets,
                TransportStreamRecordingRate = clpi?.ClipInfo.TransportStreamRecordingRate,
                Streams = clpi is not null ? CreateClpiStreams(clpi) : null,
            });
        }

        return clips;
    }

    private static ManifestTitle CreateBluRayTitle(
        string path,
        MplsPlaylist playlist,
        IReadOnlyList<NormalizedFile> files,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip,
        ICollection<ManifestDiagnostic> diagnostics,
        int? part = null)
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
                segments.Add(new ManifestSegment
                {
                    Clip = clip.value.ClipId,
                    StartSeconds = TicksToSeconds(cumulativeTicks),
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

        long presentationTicks = cumulativeTicks + GetTrailingClipTicks(playlist, clpiByClip);
        var chapters = CreateBluRayChapters(playlist, diagnostics, path, presentationTicks);
        var streams = CreateBluRayStreams(playlist, clpiByClip);
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
            DurationSeconds = TicksToSeconds(presentationTicks),
            SizeBytes = ComputeTitleSizeBytes(segments, stereoscopic3D, files),
            Chapters = chapters.Count > 0 ? chapters : null,
            Segments = segments,
            Streams = streams.Count > 0 ? streams : null,
            Stereoscopic3D = stereoscopic3D,
            Extensions = CreateUnsupportedExtensionEvidence(playlist),
        };
    }

    /// <summary>
    /// Drops play items that repeat the play item immediately before them: same clip,
    /// same in-time and same out-time. Discs loop background video this way, and MakeMKV
    /// lists each repeat once: Spartacus' <c>00020.mpls</c> plays clip 6 then clip 10 122
    /// times and MakeMKV reports segments <c>6,10</c> lasting 2:01; 1917's
    /// <c>00149.mpls</c> plays clip 174 then clip 175 251 times and MakeMKV reports
    /// <c>174,175</c>. Marks on dropped play items are dropped too. Stereoscopic
    /// playlists are left alone, because their pairing is declared per play item.
    /// </summary>
    internal static MplsPlaylist CollapseRepeatedPlayItems(MplsPlaylist playlist)
    {
        var items = playlist.PlayItems.OrderBy(item => item.Index).ToArray();
        if (items.Length < 2 || playlist.StereoVideoRelationships.Count > 0)
        {
            return playlist;
        }

        var kept = new List<MplsPlayItem>(items.Length) { items[0] };
        for (int index = 1; index < items.Length; index++)
        {
            var previous = kept[^1];
            var item = items[index];
            bool repeats = item.Clips.Count <= 1
                && previous.Clips.Count <= 1
                && string.Equals(item.ClipId, previous.ClipId, StringComparison.OrdinalIgnoreCase)
                && item.InTime == previous.InTime
                && item.OutTime == previous.OutTime;
            if (!repeats)
            {
                kept.Add(item);
            }
        }

        if (kept.Count == items.Length)
        {
            return playlist;
        }

        var indexMap = kept
            .Select((item, newIndex) => (item.Index, newIndex))
            .ToDictionary(pair => pair.Index, pair => pair.newIndex);
        return playlist with
        {
            PlayItems = kept.Select((item, newIndex) => item with { Index = newIndex }).ToArray(),
            Marks = playlist.Marks
                .Where(mark => indexMap.ContainsKey(mark.PlayItemReference))
                .OrderBy(mark => mark.Index)
                .Select((mark, newIndex) => mark with
                {
                    Index = newIndex,
                    PlayItemReference = indexMap[mark.PlayItemReference],
                })
                .ToArray(),
        };
    }

    /// <summary>
    /// Returns how much of the last play item's clip the playlist leaves unplayed after
    /// its out-time. MakeMKV reports such a title with the clip's full length, and
    /// places its last chapter accordingly: a playlist ending 4.4 s before its clip ends
    /// reads 1:33 in MakeMKV where the play item alone lasts 1:29. Only a single-angle
    /// last play item over a clip with one STC sequence is extended, because only then
    /// is the clip's presentation end on the same timeline as the out-time.
    /// </summary>
    internal static long GetTrailingClipTicks(
        MplsPlaylist playlist,
        IReadOnlyDictionary<string, ClpiFile> clpiByClip)
    {
        var last = playlist.PlayItems.OrderBy(item => item.Index).LastOrDefault();
        if (last is null
            || last.Clips.Count > 1
            || !clpiByClip.TryGetValue(last.ClipId, out var clpi)
            || clpi.PresentationSummary is not { StcSequenceCount: 1 } summary
            || last.OutTime < last.InTime
            || last.OutTime < summary.StartTime
            || summary.EndTime <= last.OutTime)
        {
            return 0;
        }

        return summary.EndTime - last.OutTime;
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
                StartSeconds = TicksToSeconds(startTicks),
            });
            chapterStartTicks.Add(startTicks);
        }

        for (int i = 0; i < chapters.Count; i++)
        {
            long chapterEndTicks = i + 1 < chapters.Count
                ? chapterStartTicks[i + 1]
                : totalDurationTicks45k;
            long durationTicks = Math.Max(0, chapterEndTicks - chapterStartTicks[i]);
            chapters[i] = chapters[i] with { DurationSeconds = TicksToSeconds(durationTicks) };
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
            int? formatCode = clpi?.FormatCode ?? stream.FormatCode;
            int? rateCode = clpi?.RateCode ?? stream.RateCode;
            streams.Add(new ManifestStream
            {
                Type = MapStreamType(stream.Category),
                Codec = clpi?.CodingType ?? stream.CodingType,
                Category = MapStreamCategory(stream.Category),
                Pid = stream.Pid,
                CodingTypeCode = clpi?.CodingTypeCode ?? stream.CodingTypeCode,
                FormatCode = formatCode,
                RateCode = rateCode,
                DynamicRangeTypeCode = clpi?.DynamicRangeTypeCode,
                ColorSpaceCode = clpi?.ColorSpaceCode,
                HdrPlusFlag = clpi?.HdrPlusFlag,
                Language = NormalizeLanguage(clpi?.LanguageCode ?? stream.LanguageCode),
                Resolution = MapVideoResolution(formatCode),
                AspectRatio = MapAspectRatio(clpi?.AspectCode),
                FrameRate = MapFrameRate(rateCode),
                IsInterlaced = MapIsInterlaced(formatCode),
                SampleRate = MapSampleRate(rateCode, stream.Category),
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
                Type = MapStreamType(stream.Category),
                Codec = stream.CodingType,
                Category = MapStreamCategory(stream.Category),
                Pid = stream.Pid,
                CodingTypeCode = stream.CodingTypeCode,
                FormatCode = stream.FormatCode,
                RateCode = stream.RateCode,
                DynamicRangeTypeCode = stream.DynamicRangeTypeCode,
                ColorSpaceCode = stream.ColorSpaceCode,
                HdrPlusFlag = stream.HdrPlusFlag,
                Language = NormalizeLanguage(stream.LanguageCode),
                Resolution = MapVideoResolution(stream.FormatCode),
                AspectRatio = MapAspectRatio(stream.AspectCode),
                FrameRate = MapFrameRate(stream.RateCode),
                IsInterlaced = MapIsInterlaced(stream.FormatCode),
                SampleRate = MapSampleRate(stream.RateCode, stream.Category),
            });
        }

        return streams;
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

    private static IOpticalDiscReader CreateReader(byte[] bytes)
        => new MemoryOpticalDiscReader(bytes);

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

    private static string MapStreamType(string category)
        => category switch
        {
            "Video" or "SecondaryVideo" or "DolbyVisionVideo" => "video",
            "Audio" or "SecondaryAudio" => "audio",
            "PresentationGraphics" or "PictureInPicturePresentationGraphics" or "Subpicture" => "subtitle",
            "InteractiveGraphics" => "menu",
            _ => "unknown",
        };

    private static string? MapStreamCategory(string category)
        => category switch
        {
            "Video" => "video",
            "SecondaryVideo" => "secondaryVideo",
            "Audio" => "audio",
            "SecondaryAudio" => "secondaryAudio",
            "PresentationGraphics" => "presentationGraphics",
            "InteractiveGraphics" => "interactiveGraphics",
            "DolbyVisionVideo" => "dolbyVisionVideo",
            "PictureInPicturePresentationGraphics" => "pictureInPicturePresentationGraphics",
            "Subpicture" => "subpicture",
            _ => null,
        };

    private static string? MapVideoResolution(int? formatCode)
        => formatCode switch
        {
            1 or 3 => "720x480",
            2 or 7 => "720x576",
            4 or 6 => "1920x1080",
            5 => "1280x720",
            8 => "3840x2160",
            _ => null,
        };

    private static string? MapAspectRatio(int? aspectCode)
        => aspectCode switch
        {
            2 => "4:3",
            3 => "16:9",
            _ => null,
        };

    private static double? MapFrameRate(int? rateCode)
        => rateCode switch
        {
            1 => 23.976,
            2 => 24,
            3 => 25,
            4 => 29.97,
            6 => 50,
            7 => 59.94,
            _ => null,
        };

    private static bool? MapIsInterlaced(int? formatCode)
        => formatCode switch
        {
            1 or 2 or 4 => true,
            3 or 5 or 6 or 7 or 8 => false,
            _ => null,
        };

    private static string? MapSampleRate(int? rateCode, string category)
    {
        if (category is not ("Audio" or "SecondaryAudio"))
        {
            return null;
        }

        return rateCode switch
        {
            1 => "48 kHz",
            2 => "96 kHz",
            3 => "192 kHz",
            _ => null,
        };
    }

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

    private sealed record BluRayParseResult(
        IReadOnlyList<ManifestTitle> Titles,
        bool IsUhd,
        IReadOnlyList<ManifestClip> Clips,
        string? DiscName);

    private sealed class ReadMetrics
    {
        public int FilesRead { get; set; }

        public long BytesRead { get; set; }
    }
}
