using System.Globalization;
using System.Text.RegularExpressions;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Dvd;
using TheDiscDb.OpticalDiscParsers.Dvd.Models;

namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static partial class DvdManifestBuilder
{
    internal static async Task<IReadOnlyList<ManifestTitle>> ParseDvdAsync(
        DiscFileCatalog files,
        ICollection<ManifestDiagnostic> diagnostics,
        ReadMetrics metrics,
        CancellationToken cancellationToken)
    {
        VmgiHeader? vmgi = null;
        var videoManagerResult = await ControlFileReader.ParseControlWithBackupAsync(
            files,
            "VIDEO_TS/VIDEO_TS.IFO",
            "VIDEO_TS/VIDEO_TS.BUP",
            diagnostics,
            metrics,
            cancellationToken,
            bytes => new DvdIfoParser(ControlFileReader.CreateReader(bytes)).ParseVmgiAsync().AsTask());
        if (videoManagerResult is null)
        {
            diagnostics.Add(ControlFileReader.MissingFile("VIDEO_TS/VIDEO_TS.IFO"));
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
            var result = await ControlFileReader.ParseControlWithBackupAsync(
                files,
                candidate.PrimaryPath,
                candidate.BackupPath,
                diagnostics,
                metrics,
                cancellationToken,
                bytes => new DvdIfoParser(ControlFileReader.CreateReader(bytes)).ParseVtsiAsync(titleSet).AsTask());
            if (result?.Value is null)
            {
                continue;
            }

            titleSets[titleSet] = result.Value;
        }

        return BuildTitles(vmgi, titleSets, diagnostics);
    }

    internal static IReadOnlyList<ManifestTitle> BuildTitles(
        VmgiHeader? vmgi,
        IReadOnlyDictionary<int, VtsiHeader> titleSets,
        ICollection<ManifestDiagnostic> diagnostics)
    {
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
            titleSets.TryGetValue(title.TitleSetNumber, out var vtsi);
            var result = CreateTitle(title, vtsi, diagnostics);
            if (result.Title is not null)
            {
                titles.Add(result.Title);
            }
            else if (result.Disposition == DvdTitleDisposition.Skipped)
            {
                skippedTitles++;
            }
        }

        if (titles.Count + skippedTitles != vmgi.Titles.Count)
        {
            diagnostics.Add(DvdPartial(
                $"Only {titles.Count} of {vmgi.Titles.Count} VMGI logical titles were joined successfully."));
        }

        return titles;
    }

    private static DvdTitleResult CreateTitle(
        VmgiTitle title,
        VtsiHeader? vtsi,
        ICollection<ManifestDiagnostic> diagnostics)
    {
        if (vtsi is null)
        {
            diagnostics.Add(DvdPartial(
                $"Logical title {title.Number} references missing title set {title.TitleSetNumber}."));
            return new DvdTitleResult(null, DvdTitleDisposition.Unresolved);
        }

        var titleMap = vtsi.TitlePartMaps.SingleOrDefault(
            item => item.TitleSetTitleNumber == title.TitleSetTitleNumber);
        if (titleMap is null)
        {
            diagnostics.Add(DvdPartial(
                $"Logical title {title.Number} references missing VTS title {title.TitleSetTitleNumber} in title set {title.TitleSetNumber}."));
            return new DvdTitleResult(null, DvdTitleDisposition.Unresolved);
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
                StartSeconds = ManifestTiming.RoundSeconds(elapsedSeconds),
                DurationSeconds = ManifestTiming.RoundSeconds(duration),
            });

            double cellStart = elapsedSeconds;
            foreach (var cell in playedCells)
            {
                var position = pgc.CellPositions[cell.Number - 1];
                double cellSeconds = cell.PlaybackTime.ToTimeSpan().TotalSeconds;
                segments.Add(new ManifestSegment
                {
                    Clip = FormatDvdCellClip(position),
                    StartSeconds = ManifestTiming.RoundSeconds(cellStart),
                    DurationSeconds = ManifestTiming.RoundSeconds(cellSeconds),
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
            return new DvdTitleResult(null, DvdTitleDisposition.Skipped);
        }

        // MakeMKV declares a DVD title's length as the sum of its cells' whole seconds and
        // treats a title declared 0:00:00 as fake ("declared length is 0:00:00 ... assuming
        // fake title", MSG 3026, about 1,500 times across the TheDiscDb log corpus), so it is
        // not listed: Avatar's titles 2-4 and 6-9 are built from 0.52 s cells. Only old
        // MakeMKV builds falling back to CellTrim ever listed 0:00:00 DVD titles.
        if (timingComplete
            && segments.Count > 0
            && segments.Sum(segment => Math.Floor(segment.DurationSeconds ?? 0)) == 0)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_DVD_TITLE_SKIPPED",
                Message = $"Logical title {title.Number} is omitted because its cells declare no whole seconds, which MakeMKV treats as a fake title.",
            });
            return new DvdTitleResult(null, DvdTitleDisposition.Skipped);
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
        return new DvdTitleResult(new ManifestTitle
        {
            Source = new ManifestTitleSource
            {
                Title = title.Number,
                TitleSet = title.TitleSetNumber,
                TitleSetTitle = title.TitleSetTitleNumber,
            },
            DurationSeconds = timingComplete ? ManifestTiming.RoundSeconds(elapsedSeconds) : null,
            SizeBytes = sizeComplete && completeParts && titleMap.Parts.Count > 0 ? sizeBytes : null,
            Chapters = chapters.Count > 0 && timingComplete ? chapters : null,
            Segments = segments.Count > 0 && timingComplete ? segments : null,
            Streams = streams.Count > 0 ? streams : null,
        }, DvdTitleDisposition.Joined);
    }

    private enum DvdTitleDisposition
    {
        Joined,
        Skipped,
        Unresolved,
    }

    private sealed record DvdTitleResult(ManifestTitle? Title, DvdTitleDisposition Disposition);

    private const long DvdSectorSizeBytes = 2048;

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
                Language = ManifestStreamMapper.NormalizeLanguage(stream.LanguageCode),
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
                Language = ManifestStreamMapper.NormalizeLanguage(stream.LanguageCode),
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

    [GeneratedRegex(@"^VIDEO_TS/VTS_(\d{2})_0\.(IFO|BUP)$", RegexOptions.IgnoreCase)]
    private static partial Regex VtsControlPattern();

    private sealed record DvdTitleSetCandidate(int TitleSetNumber, string PrimaryPath, string BackupPath);
}