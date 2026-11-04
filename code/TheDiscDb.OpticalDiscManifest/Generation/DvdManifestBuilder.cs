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
        int joinedTitles = 0;
        foreach (var title in vmgi.Titles.OrderBy(item => item.Number))
        {
            titleSets.TryGetValue(title.TitleSetNumber, out var vtsi);

            // MakeMKV lists a multi-angle title once per angle ("Angle #2 was added for title
            // #3", MSG 3040), each playing the shared cells plus that angle's cell of every
            // angle block.
            int angleTitles = CountAngleTitles(title, vtsi);
            bool joined = false;
            bool skipped = false;
            for (int angle = 1; angle <= angleTitles; angle++)
            {
                var titleDiagnostics = angle == 1 ? diagnostics : new List<ManifestDiagnostic>();
                var result = CreateTitle(title, vtsi, titleDiagnostics, angleTitles > 1 ? angle : null);
                foreach (var diagnostic in titleDiagnostics.Where(item => !diagnostics.Contains(item)))
                {
                    diagnostics.Add(diagnostic);
                }

                if (result.Title is not null)
                {
                    titles.Add(result.Title);
                    joined = true;
                }
                else if (result.Disposition == DvdTitleDisposition.Skipped)
                {
                    skipped = true;
                }
            }

            if (joined)
            {
                joinedTitles++;
            }
            else if (skipped)
            {
                skippedTitles++;
            }
        }

        if (joinedTitles + skippedTitles != vmgi.Titles.Count)
        {
            diagnostics.Add(DvdPartial(
                $"Only {joinedTitles} of {vmgi.Titles.Count} VMGI logical titles were joined successfully."));
        }

        return titles;
    }

    private static DvdTitleResult CreateTitle(
        VmgiTitle title,
        VtsiHeader? vtsi,
        ICollection<ManifestDiagnostic> diagnostics,
        int? angle = null)
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

        var titleParts = titleMap.Parts.OrderBy(item => item.Number).ToList();
        bool timingSupported = title.NumberOfAngles == 1 || angle is not null;
        if (timingSupported && title.PlaybackFlags.IsMultiOrRandomPgcTitle)
        {
            var selectedPgc = SelectMultiPgcTitleProgramChain(vtsi, titleParts);
            if (selectedPgc is null)
            {
                timingSupported = false;
            }
            else
            {
                titleParts = titleParts.Where(part => part.ProgramChainNumber == selectedPgc.Value).ToList();
            }
        }

        var chapters = new List<ManifestChapter>(titleParts.Count);
        var segments = new List<ManifestSegment>();
        double elapsedSeconds = 0;
        long sizeBytes = 0;
        bool sizeComplete = timingSupported;
        bool timingComplete = timingSupported && completeParts;
        bool anyChapterMark = false;
        bool anyCells = false;
        bool joinsComplete = completeParts;
        bool rangesComplete = true;
        double declaredWholeSeconds = 0;
        foreach (var part in titleParts)
        {
            var pgc = vtsi.ProgramChains.SingleOrDefault(
                item => item.Number == part.ProgramChainNumber);
            if (pgc is null || part.ProgramNumber < 1 || part.ProgramNumber > pgc.NumberOfPrograms)
            {
                diagnostics.Add(DvdPartial(
                    $"Logical title {title.Number} part {part.Number} references unavailable PGC {part.ProgramChainNumber}, program {part.ProgramNumber}."));
                timingComplete = false;
                sizeComplete = false;
                joinsComplete = false;
                chapters.Add(new ManifestChapter { StartSeconds = 0 });
                continue;
            }

            if (!TryGetProgramCells(pgc, part.ProgramNumber, out var programCells))
            {
                rangesComplete = false;
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

            int firstPlayedCellNumber = playedCells[0].Number;
            var angleCells = angle is null ? null : GetAngleCells(pgc, title.NumberOfAngles);
            if (angleCells is not null)
            {
                playedCells = playedCells
                    .Where(cell => !angleCells.TryGetValue(cell.Number, out var angleCell) || angleCell.Angle == angle)
                    .ToList();
            }

            declaredWholeSeconds += playedCells.Sum(cell => Math.Floor(cell.PlaybackTime.ToTimeSpan().TotalSeconds));

            if (sizeComplete && TryGetCellSizeBytes(playedCells, angleCells, out long programBytes))
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
            anyChapterMark |= firstPlayedCellNumber == programCells[0].Number;
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
                    Angle = angleCells?.ContainsKey(cell.Number) == true ? angle : null,
                    Cell = cell.Number,
                });
                cellStart += cellSeconds;
            }

            elapsedSeconds += duration;
        }

        bool singlePerspective = title.NumberOfAngles == 1 || angle is not null;
        if (singlePerspective && joinsComplete && !anyCells)
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
        if (singlePerspective && joinsComplete && rangesComplete && anyCells && declaredWholeSeconds == 0)
        {
            diagnostics.Add(new ManifestDiagnostic
            {
                Severity = "info",
                Code = "ODM_DVD_TITLE_SKIPPED",
                Message = $"Logical title {title.Number} is omitted because its cells declare no whole seconds, which MakeMKV treats as a fake title.",
            });
            return new DvdTitleResult(null, DvdTitleDisposition.Skipped);
        }

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
            chapters = titleParts
                .Select(_ => new ManifestChapter { StartSeconds = 0 })
                .ToList();
        }

        var titleProgramChains = new List<ProgramChain>();
        foreach (var part in titleParts)
        {
            var pgc = vtsi.ProgramChains.SingleOrDefault(item => item.Number == part.ProgramChainNumber);
            if (pgc is null)
            {
                titleProgramChains.Clear();
                break;
            }

            if (!titleProgramChains.Contains(pgc))
            {
                titleProgramChains.Add(pgc);
            }
        }

        var streams = CreateDvdStreams(vtsi, titleProgramChains);
        return new DvdTitleResult(new ManifestTitle
        {
            Source = new ManifestTitleSource
            {
                Title = title.Number,
                TitleSet = title.TitleSetNumber,
                TitleSetTitle = title.TitleSetTitleNumber,
            },
            DurationSeconds = timingComplete ? ManifestTiming.RoundSeconds(elapsedSeconds) : null,
            SizeBytes = sizeComplete && completeParts && titleParts.Count > 0 ? sizeBytes : null,
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
    /// MakeMKV lists the PGCs a multi-PGC title actually plays, following its pre/post/cell link
    /// commands from the entry PGC, as titles "#N", "#N/1", ... and drops those declaring 0 s.
    /// Without emulating those commands, the first PGC in part order whose played cells declare
    /// at least one whole second reproduces every case where it lists one: Man on Fire (2004)
    /// disc 1 title 3 (PGC 1, 2:30, whose post command links to a PGC that jumps out unplayed),
    /// seven titles on two other local captures that skip 0 s language-select PGCs 1-2 and match
    /// PGC 3 in bytes, cells and chapters, and the Fox logo titles on Die Hard (1988) and
    /// Bedazzled (2000). Titles whose path plays several PGCs (Bedazzled's #8 ... #8/7) are not
    /// split. A title whose PGCs all declare 0 s returns null, to be omitted as a fake title.
    /// </summary>
    private static int? SelectMultiPgcTitleProgramChain(VtsiHeader vtsi, IEnumerable<VtsPartOfTitle> parts)
    {
        foreach (int number in parts.Select(part => part.ProgramChainNumber).Distinct())
        {
            var pgc = vtsi.ProgramChains.SingleOrDefault(item => item.Number == number);
            if (pgc is null || pgc.CellPlayback.Count == 0)
            {
                return null;
            }

            var (first, last) = GetPlayedCellRange(pgc);
            double wholeSeconds = pgc.CellPlayback
                .Where(cell => cell.Number >= first && cell.Number <= last)
                .Sum(cell => Math.Floor(cell.PlaybackTime.ToTimeSpan().TotalSeconds));
            if (wholeSeconds > 0)
            {
                return number;
            }
        }

        return null;
    }

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
    private static bool TryGetCellSizeBytes(
        IEnumerable<CellPlaybackInfo> cells,
        IReadOnlyDictionary<int, DvdAngleCell>? angleCells,
        out long sizeBytes)
    {
        sizeBytes = 0;
        foreach (var cell in cells)
        {
            if (angleCells is not null && angleCells.TryGetValue(cell.Number, out var angleCell))
            {
                sizeBytes += angleCell.ShareBytes;
                continue;
            }

            if (cell.LastSector < cell.FirstSector)
            {
                sizeBytes = 0;
                return false;
            }

            sizeBytes += ((long)cell.LastSector - cell.FirstSector + 1) * DvdSectorSizeBytes;
        }

        return true;
    }

    private sealed record DvdAngleCell(int Angle, long ShareBytes);

    /// <summary>
    /// Returns how many titles MakeMKV lists for a logical title: one per angle when every PGC
    /// the title references lays its angle blocks out as one cell per declared angle, otherwise
    /// one. Multi-PGC titles qualify too (Die Hard (1988)'s "Angle #2 was added for title #12/1").
    /// </summary>
    private static int CountAngleTitles(VmgiTitle title, VtsiHeader? vtsi)
    {
        if (title.NumberOfAngles < 2 || vtsi is null)
        {
            return 1;
        }

        var titleMap = vtsi.TitlePartMaps.SingleOrDefault(
            item => item.TitleSetTitleNumber == title.TitleSetTitleNumber);
        if (titleMap is null || titleMap.Parts.Count == 0)
        {
            return 1;
        }

        bool anyAngleBlock = false;
        foreach (int pgcNumber in titleMap.Parts.Select(part => part.ProgramChainNumber).Distinct())
        {
            var pgc = vtsi.ProgramChains.SingleOrDefault(item => item.Number == pgcNumber);
            var angleCells = pgc is null ? null : GetAngleCells(pgc, title.NumberOfAngles);
            if (angleCells is null)
            {
                return 1;
            }

            anyAngleBlock |= angleCells.Count > 0;
        }

        return anyAngleBlock ? title.NumberOfAngles : 1;
    }

    /// <summary>
    /// Maps each angle-block cell of a PGC to its angle and its share of the block's bytes, or
    /// returns null when a block does not hold exactly one cell per angle. Angle cells are
    /// interleaved, so their sector ranges overlap; MakeMKV's size for each angle of Kiki's
    /// Delivery Service (2 angles, 5,502,390,272 bytes) is the shared cells plus an even share of
    /// every angle block's sector span.
    /// </summary>
    private static Dictionary<int, DvdAngleCell>? GetAngleCells(ProgramChain pgc, int angles)
    {
        var result = new Dictionary<int, DvdAngleCell>();
        var block = new List<CellPlaybackInfo>();
        foreach (var cell in pgc.CellPlayback)
        {
            if (cell.BlockType != DvdCellBlockType.AngleBlock)
            {
                if (block.Count > 0)
                {
                    return null;
                }

                continue;
            }

            if ((cell.BlockMode == DvdCellBlockMode.FirstCell) != (block.Count == 0)
                || cell.BlockMode == DvdCellBlockMode.NotInBlock
                || cell.LastSector < cell.FirstSector)
            {
                return null;
            }

            block.Add(cell);
            if (cell.BlockMode != DvdCellBlockMode.LastCell)
            {
                continue;
            }

            if (block.Count != angles)
            {
                return null;
            }

            long spanSectors = (long)block.Max(item => item.LastSector) - block.Min(item => item.FirstSector) + 1;
            long shareBytes = spanSectors * DvdSectorSizeBytes / angles;
            for (int index = 0; index < block.Count; index++)
            {
                result[block[index].Number] = new DvdAngleCell(index + 1, shareBytes);
            }

            block.Clear();
        }

        return block.Count == 0 ? result : null;
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

    private static IReadOnlyList<ManifestStream> CreateDvdStreams(
        VtsiHeader vtsi,
        IReadOnlyList<ProgramChain> programChains)
    {
        // A title plays only the audio and subpicture streams its PGCs mark available, and
        // MakeMKV lists one stream per distinct physical stream mapping: Rebel Without a
        // Cause's extras map all three logical subpicture streams to physical stream 0 and its
        // trailer enables only audio stream 3; Contagion's disc 2 title 2 maps three logical
        // audio streams to physical stream 0. Without resolved PGCs, keep every VTS stream.
        bool usePgcControls = programChains.Count > 0;
        var availableAudio = new HashSet<int>();
        var audioMappings = new HashSet<(string? Codec, int Number)>();
        foreach (var control in programChains
            .SelectMany(pgc => pgc.AudioControls)
            .Where(control => control.IsAvailable)
            .OrderBy(control => control.StreamIndex))
        {
            // Physical audio numbers are per coding type (AC-3 0x80+n, DTS 0x88+n, LPCM 0xA0+n).
            string? codec = vtsi.AudioStreams.FirstOrDefault(item => item.Index == control.StreamIndex)?.Codec;
            if (audioMappings.Add((codec, control.PlayerStreamNumber ?? control.StreamIndex)))
            {
                availableAudio.Add(control.StreamIndex);
            }
        }
        var availableSubpictures = new HashSet<int>();
        var subpictureMappings = new HashSet<uint>();
        foreach (var control in programChains
            .SelectMany(pgc => pgc.SubpictureControls)
            .Where(control => control.IsAvailable)
            .OrderBy(control => control.StreamIndex))
        {
            if (subpictureMappings.Add(control.RawValue & 0x1F1F1F1F))
            {
                availableSubpictures.Add(control.StreamIndex);
            }
        }

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
        foreach (var stream in vtsi.AudioStreams
            .Where(item => !usePgcControls || availableAudio.Contains(item.Index))
            .OrderBy(item => item.Index))
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

        foreach (var stream in vtsi.SubtitleStreams
            .Where(item => !usePgcControls || availableSubpictures.Contains(item.Index))
            .OrderBy(item => item.Index))
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