namespace TheDiscDb.OpticalDiscParsers.Dvd;

using System.Buffers.Binary;
using System.Text;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Dvd.Models;
using TheDiscDb.OpticalDiscParsers.Input;
using TheDiscDb.OpticalDiscParsers.Models;

/// <summary>
/// Parses DVD-Video IFO (Information) files in VMGI (Video Manager Information) or VTSI (Video Title Set Information) format.
/// </summary>
public sealed class DvdIfoParser
{
    private readonly IOpticalDiscReader source;

    /// <summary>
    /// Creates a new DVD IFO parser.
    /// </summary>
    public DvdIfoParser(IOpticalDiscReader source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// Parses a VMGI (Video Manager Information) file and returns its header information.
    /// </summary>
    public ValueTask<ParserResult<VmgiHeader>> ParseVmgiAsync()
    {
        var context = new ParseContext(source);
        return new DvdIfoParseSession(context).ParseVmgiAsync();
    }

    /// <summary>
    /// Parses a VTSI (Video Title Set Information) file and returns its header information.
    /// </summary>
    public ValueTask<ParserResult<VtsiHeader>> ParseVtsiAsync(int titleSetNumber = 1)
    {
        var context = new ParseContext(source);
        return new DvdIfoParseSession(context).ParseVtsiAsync(titleSetNumber);
    }
}

internal sealed class DvdIfoParseSession
{
    private const int SectorSize = 2048;
    private const int MaximumDvdTitles = 99;
    private const int MaximumDvdTitleSets = 99;
    private const int MaximumProgramChains = 999;
    private const int PgcHeaderSize = 236;
    private const int CellPlaybackRecordSize = 24;
    private const int CellPositionRecordSize = 4;

    private readonly OpticalDiscBinaryReader reader;
    private readonly DiagnosticBag diagnostics;

    public DvdIfoParseSession(ParseContext context)
    {
        reader = context.Reader;
        diagnostics = context.Diagnostics;
    }

    public async ValueTask<ParserResult<VmgiHeader>> ParseVmgiAsync()
    {
        string? identifier = await ReadStringAtAsync(0x00, 12);
        if (identifier is null)
        {
            diagnostics.Error(0, "VD001", "Invalid or missing VMGI identifier");
            return new ParserResult<VmgiHeader>(null, diagnostics.Snapshot());
        }

        if (!identifier.StartsWith("DVDVIDEO-VMG", StringComparison.Ordinal))
        {
            diagnostics.Error(0, "VD002", $"Expected VMGI identifier 'DVDVIDEO-VMG', got '{identifier}'");
            return new ParserResult<VmgiHeader>(null, diagnostics.Snapshot());
        }

        ushort specVersion = await ReadSpecificationVersionAsync();
        string providerId = await ReadStringAtAsync(0x40, 32) ?? "Unknown";
        uint lastSectorAddress = await ReadUInt32AtAsync(0x1C);
        int numberOfTitleSets = await ReadUInt16AtAsync(0x3E);
        if (numberOfTitleSets > MaximumDvdTitleSets)
        {
            diagnostics.Warning(0x3E, "VD004", $"Unusually high title set count: {numberOfTitleSets}");
            numberOfTitleSets = MaximumDvdTitleSets;
        }

        byte regionMask = await ReadByteAtAsync(0xC2);
        if (regionMask == 0)
        {
            regionMask = 0xFF;
        }

        IReadOnlyList<VmgiTitle> titles = await ParseVmgiTitlesAsync();
        IReadOnlyList<TitleSetReference> titleSets = CreateTitleSetReferences(titles, numberOfTitleSets);

        var header = new VmgiHeader
        {
            Identifier = identifier,
            SpecificationVersion = specVersion,
            ProviderId = providerId,
            LastSectorAddress = lastSectorAddress,
            NumberOfVolumeTitles = titles.Count,
            NumberOfTitleSets = numberOfTitleSets,
            NumberOfVmgMenus = 0,
            RegionMask = regionMask,
            NumberOfAccessibleMenus = 0,
            TitleSets = titleSets,
            Titles = titles
        };

        return new ParserResult<VmgiHeader>(header, diagnostics.Snapshot());
    }

    public async ValueTask<ParserResult<VtsiHeader>> ParseVtsiAsync(int titleSetNumber)
    {
        string? identifier = await ReadStringAtAsync(0x00, 12);
        if (identifier is null)
        {
            diagnostics.Error(0, "VD010", "Invalid or missing VTSI identifier");
            return new ParserResult<VtsiHeader>(null, diagnostics.Snapshot());
        }

        if (!identifier.StartsWith("DVDVIDEO-VTS", StringComparison.Ordinal))
        {
            diagnostics.Error(0, "VD011", $"Expected VTSI identifier 'DVDVIDEO-VTS', got '{identifier}'");
            return new ParserResult<VtsiHeader>(null, diagnostics.Snapshot());
        }

        ushort specVersion = await ReadSpecificationVersionAsync();
        string providerId = await ReadStringAtAsync(0x40, 32) ?? "Unknown";
        uint lastSectorAddress = await ReadUInt32AtAsync(0x1C);

        List<VideoStreamInfo> videoStreams = await ParseVideoStreamsAsync();
        List<AudioStreamInfo> audioStreams = await ParseAudioStreamsAsync();
        List<SubtitleStreamInfo> subtitleStreams = await ParseSubtitleStreamsAsync();
        IReadOnlyList<VtsTitlePartMap> titlePartMaps = await ParseTitlePartMapsAsync();
        IReadOnlyList<ProgramChain> programChains = await ParseProgramChainsAsync();

        var header = new VtsiHeader
        {
            Identifier = identifier,
            TitleSetNumber = titleSetNumber,
            SpecificationVersion = specVersion,
            ProviderId = providerId,
            LastSectorAddress = lastSectorAddress,
            NumberOfProgramChains = programChains.Count,
            NumberOfPrograms = programChains.Sum(item => item.NumberOfPrograms),
            NumberOfCells = programChains.Sum(item => item.NumberOfCells),
            NumberOfMenus = 0,
            VideoStreams = videoStreams,
            AudioStreams = audioStreams,
            SubtitleStreams = subtitleStreams,
            ProgramChains = programChains,
            TitlePartMaps = titlePartMaps
        };

        return new ParserResult<VtsiHeader>(header, diagnostics.Snapshot());
    }

    private async ValueTask<IReadOnlyList<VmgiTitle>> ParseVmgiTitlesAsync()
    {
        uint sector = await ReadUInt32AtAsync(0xC4);
        if (sector == 0)
        {
            diagnostics.Warning(0xC4, "VD020", "VMGI TT_SRPT pointer is zero; no logical titles were parsed");
            return Array.Empty<VmgiTitle>();
        }

        long tableOffset = SectorToByteOffset(sector);
        ReadOnlyMemory<byte> header = await ReadBytesAtAsync(tableOffset, 8, "VMGI TT_SRPT header");
        if (header.Length < 8)
        {
            diagnostics.Error(tableOffset, "VD021", "VMGI TT_SRPT header is truncated");
            return Array.Empty<VmgiTitle>();
        }

        int titleCount = BinaryPrimitives.ReadUInt16BigEndian(header.Span);
        uint lastByte = BinaryPrimitives.ReadUInt32BigEndian(header.Span[4..8]);
        if (titleCount > MaximumDvdTitles)
        {
            diagnostics.Warning(tableOffset, "VD022", $"VMGI TT_SRPT title count {titleCount} exceeds DVD maximum; truncating to {MaximumDvdTitles}");
            titleCount = MaximumDvdTitles;
        }

        int tableLength = GetTableLength(lastByte, 8 + (titleCount * 12), "VMGI TT_SRPT", tableOffset);
        ReadOnlyMemory<byte> table = await ReadBytesAtAsync(tableOffset, tableLength, "VMGI TT_SRPT");
        var titles = new List<VmgiTitle>(titleCount);
        for (int i = 0; i < titleCount; i++)
        {
            int entryOffset = 8 + (i * 12);
            if (!HasRange(table.Span, entryOffset, 12))
            {
                diagnostics.Error(tableOffset + entryOffset, "VD023", $"VMGI TT_SRPT title entry {i + 1} is truncated");
                break;
            }

            ReadOnlySpan<byte> entry = table.Span.Slice(entryOffset, 12);
            byte playbackType = entry[0];
            titles.Add(new VmgiTitle
            {
                Number = i + 1,
                PlaybackFlags = CreateTitlePlaybackFlags(playbackType),
                NumberOfAngles = entry[1],
                NumberOfPartsOfTitle = BinaryPrimitives.ReadUInt16BigEndian(entry[2..4]),
                ParentalManagementMask = BinaryPrimitives.ReadUInt16BigEndian(entry[4..6]),
                TitleSetNumber = entry[6],
                TitleSetTitleNumber = entry[7],
                TitleSetSector = BinaryPrimitives.ReadUInt32BigEndian(entry[8..12])
            });
        }

        return titles;
    }

    private async ValueTask<IReadOnlyList<VtsTitlePartMap>> ParseTitlePartMapsAsync()
    {
        uint sector = await ReadUInt32AtAsync(0xC8);
        if (sector == 0)
        {
            diagnostics.Warning(0xC8, "VD030", "VTS_PTT_SRPT pointer is zero; no title/chapter mappings were parsed");
            return Array.Empty<VtsTitlePartMap>();
        }

        long tableOffset = SectorToByteOffset(sector);
        ReadOnlyMemory<byte> header = await ReadBytesAtAsync(tableOffset, 8, "VTS_PTT_SRPT header");
        if (header.Length < 8)
        {
            diagnostics.Error(tableOffset, "VD031", "VTS_PTT_SRPT header is truncated");
            return Array.Empty<VtsTitlePartMap>();
        }

        int titleCount = BinaryPrimitives.ReadUInt16BigEndian(header.Span);
        uint lastByte = BinaryPrimitives.ReadUInt32BigEndian(header.Span[4..8]);
        if (titleCount > MaximumDvdTitles)
        {
            diagnostics.Warning(tableOffset, "VD032", $"VTS_PTT_SRPT title count {titleCount} exceeds DVD maximum; truncating to {MaximumDvdTitles}");
            titleCount = MaximumDvdTitles;
        }

        int minimumLength = 8 + (titleCount * 4);
        int tableLength = GetTableLength(lastByte, minimumLength, "VTS_PTT_SRPT", tableOffset);
        ReadOnlyMemory<byte> table = await ReadBytesAtAsync(tableOffset, tableLength, "VTS_PTT_SRPT");
        if (table.Length < minimumLength)
        {
            diagnostics.Error(tableOffset, "VD033", "VTS_PTT_SRPT offset table is truncated");
            return Array.Empty<VtsTitlePartMap>();
        }

        var offsets = new List<int>(titleCount);
        for (int i = 0; i < titleCount; i++)
        {
            int offsetPosition = 8 + (i * 4);
            offsets.Add((int)BinaryPrimitives.ReadUInt32BigEndian(table.Span.Slice(offsetPosition, 4)));
        }

        var maps = new List<VtsTitlePartMap>(titleCount);
        for (int i = 0; i < titleCount; i++)
        {
            int titleStart = offsets[i];
            int titleEnd = i + 1 < offsets.Count ? offsets[i + 1] : (int)Math.Min(lastByte + 1UL, (ulong)table.Length);
            if (!ValidateRelativeRange(table.Span, titleStart, titleEnd, minimumLength, tableOffset, "VTS_PTT_SRPT", i + 1))
            {
                maps.Add(new VtsTitlePartMap
                {
                    TitleSetTitleNumber = i + 1,
                    Parts = Array.Empty<VtsPartOfTitle>()
                });
                continue;
            }

            int partBytes = titleEnd - titleStart;
            if (partBytes % 4 != 0)
            {
                diagnostics.Warning(tableOffset + titleStart, "VD034", $"VTS_PTT_SRPT title {i + 1} has {partBytes} bytes; ignoring trailing {partBytes % 4} byte(s)");
            }

            var parts = new List<VtsPartOfTitle>(partBytes / 4);
            for (int partIndex = 0; partIndex + 4 <= partBytes; partIndex += 4)
            {
                ReadOnlySpan<byte> entry = table.Span.Slice(titleStart + partIndex, 4);
                parts.Add(new VtsPartOfTitle
                {
                    Number = (partIndex / 4) + 1,
                    ProgramChainNumber = BinaryPrimitives.ReadUInt16BigEndian(entry[..2]),
                    ProgramNumber = BinaryPrimitives.ReadUInt16BigEndian(entry[2..4])
                });
            }

            maps.Add(new VtsTitlePartMap
            {
                TitleSetTitleNumber = i + 1,
                Parts = parts
            });
        }

        return maps;
    }

    private async ValueTask<IReadOnlyList<ProgramChain>> ParseProgramChainsAsync()
    {
        uint sector = await ReadUInt32AtAsync(0xCC);
        if (sector == 0)
        {
            diagnostics.Warning(0xCC, "VD040", "VTS_PGCIT pointer is zero; no program chains were parsed");
            return Array.Empty<ProgramChain>();
        }

        long tableOffset = SectorToByteOffset(sector);
        ReadOnlyMemory<byte> header = await ReadBytesAtAsync(tableOffset, 8, "VTS_PGCIT header");
        if (header.Length < 8)
        {
            diagnostics.Error(tableOffset, "VD041", "VTS_PGCIT header is truncated");
            return Array.Empty<ProgramChain>();
        }

        int pgcCount = BinaryPrimitives.ReadUInt16BigEndian(header.Span);
        uint lastByte = BinaryPrimitives.ReadUInt32BigEndian(header.Span[4..8]);
        if (pgcCount > MaximumProgramChains)
        {
            diagnostics.Warning(tableOffset, "VD042", $"VTS_PGCIT program-chain count {pgcCount} is unusually high; truncating to {MaximumProgramChains}");
            pgcCount = MaximumProgramChains;
        }

        int minimumLength = 8 + (pgcCount * 8);
        int tableLength = GetTableLength(lastByte, minimumLength, "VTS_PGCIT", tableOffset);
        ReadOnlyMemory<byte> table = await ReadBytesAtAsync(tableOffset, tableLength, "VTS_PGCIT");
        if (table.Length < minimumLength)
        {
            diagnostics.Error(tableOffset, "VD043", "VTS_PGCIT search-pointer table is truncated");
            return Array.Empty<ProgramChain>();
        }

        var programChains = new List<ProgramChain>(pgcCount);
        for (int i = 0; i < pgcCount; i++)
        {
            int entryOffset = 8 + (i * 8);
            ReadOnlySpan<byte> entry = table.Span.Slice(entryOffset, 8);
            byte entryId = entry[0];
            byte blockFlags = entry[1];
            ushort parentalMask = BinaryPrimitives.ReadUInt16BigEndian(entry[2..4]);
            int pgcStart = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..8]);

            if (pgcStart < minimumLength || pgcStart + PgcHeaderSize > table.Length)
            {
                diagnostics.Error(tableOffset + entryOffset, "VD044", $"VTS_PGCIT PGC {i + 1} points outside the parsed table");
                continue;
            }

            programChains.Add(ParseProgramChain(table.Span, tableOffset, i + 1, entryId, blockFlags, parentalMask, pgcStart));
        }

        return programChains;
    }

    private ProgramChain ParseProgramChain(
        ReadOnlySpan<byte> table,
        long tableOffset,
        int number,
        byte entryId,
        byte blockFlags,
        ushort parentalMask,
        int pgcStart)
    {
        ReadOnlySpan<byte> pgc = table[pgcStart..];
        int numberOfPrograms = pgc[2];
        int numberOfCells = pgc[3];
        DvdPlaybackTime playbackTime = ParsePlaybackTime(pgc.Slice(4, 4));
        int nextPgc = BinaryPrimitives.ReadUInt16BigEndian(pgc[156..158]);
        int previousPgc = BinaryPrimitives.ReadUInt16BigEndian(pgc[158..160]);
        int goUpPgc = BinaryPrimitives.ReadUInt16BigEndian(pgc[160..162]);
        int commandTableOffset = BinaryPrimitives.ReadUInt16BigEndian(pgc[228..230]);
        int programMapOffset = BinaryPrimitives.ReadUInt16BigEndian(pgc[230..232]);
        int cellPlaybackOffset = BinaryPrimitives.ReadUInt16BigEndian(pgc[232..234]);
        int cellPositionOffset = BinaryPrimitives.ReadUInt16BigEndian(pgc[234..236]);

        IReadOnlyList<ProgramMapEntry> programMap = ParseProgramMap(pgc, tableOffset + pgcStart, programMapOffset, numberOfPrograms);
        IReadOnlyList<DvdAudioStreamControl> audioControls = ParseAudioControls(pgc);
        IReadOnlyList<DvdSubpictureStreamControl> subpictureControls = ParseSubpictureControls(pgc);
        IReadOnlyList<CellPlaybackInfo> cellPlayback = ParseCellPlaybackTable(pgc, tableOffset + pgcStart, cellPlaybackOffset, numberOfCells);
        IReadOnlyList<CellPositionInfo> cellPositions = ParseCellPositionTable(pgc, tableOffset + pgcStart, cellPositionOffset, numberOfCells);
        if (commandTableOffset != 0 && commandTableOffset < PgcHeaderSize)
        {
            diagnostics.Warning(tableOffset + pgcStart + commandTableOffset, "VD045", $"PGC {number} command table offset points inside the fixed PGC header");
        }

        return new ProgramChain
        {
            Number = number,
            EntryId = entryId,
            IsEntryProgramChain = (entryId & 0x80) != 0,
            BlockMode = (DvdCellBlockMode)(blockFlags & 0x03),
            BlockType = ToBlockType((blockFlags >> 2) & 0x03),
            ParentalManagementMask = parentalMask,
            NumberOfPrograms = numberOfPrograms,
            NumberOfCells = numberOfCells,
            PlaybackTime = playbackTime,
            ProgramNumbers = Enumerable.Range(1, numberOfPrograms).ToArray(),
            CellIndices = programMap.Select(item => item.FirstCellNumber).ToArray(),
            ProgramMap = programMap,
            AudioControls = audioControls,
            SubpictureControls = subpictureControls,
            CellPlayback = cellPlayback,
            CellPositions = cellPositions,
            NextProgramChainNumber = nextPgc,
            PreviousProgramChainNumber = previousPgc,
            GoUpProgramChainNumber = goUpPgc
        };
    }

    private IReadOnlyList<ProgramMapEntry> ParseProgramMap(ReadOnlySpan<byte> pgc, long pgcOffset, int offset, int count)
    {
        if (offset == 0 || count == 0)
        {
            return Array.Empty<ProgramMapEntry>();
        }

        if (!HasRange(pgc, offset, count))
        {
            diagnostics.Error(pgcOffset + offset, "VD046", "PGC program map is truncated");
            return Array.Empty<ProgramMapEntry>();
        }

        var entries = new List<ProgramMapEntry>(count);
        for (int i = 0; i < count; i++)
        {
            entries.Add(new ProgramMapEntry
            {
                ProgramNumber = i + 1,
                FirstCellNumber = pgc[offset + i]
            });
        }

        return entries;
    }

    private static IReadOnlyList<DvdAudioStreamControl> ParseAudioControls(ReadOnlySpan<byte> pgc)
    {
        var controls = new List<DvdAudioStreamControl>(8);
        for (int i = 0; i < 8; i++)
        {
            ushort rawValue = BinaryPrimitives.ReadUInt16BigEndian(pgc.Slice(12 + (i * 2), 2));
            bool isAvailable = (rawValue & 0x8000) != 0;
            controls.Add(new DvdAudioStreamControl
            {
                StreamIndex = i,
                RawValue = rawValue,
                IsAvailable = isAvailable,
                PlayerStreamNumber = isAvailable ? (rawValue >> 8) & 0x07 : null
            });
        }

        return controls;
    }

    private static IReadOnlyList<DvdSubpictureStreamControl> ParseSubpictureControls(ReadOnlySpan<byte> pgc)
    {
        var controls = new List<DvdSubpictureStreamControl>(32);
        for (int i = 0; i < 32; i++)
        {
            uint rawValue = BinaryPrimitives.ReadUInt32BigEndian(pgc.Slice(28 + (i * 4), 4));
            bool isAvailable = (rawValue & 0x80000000) != 0;
            controls.Add(new DvdSubpictureStreamControl
            {
                StreamIndex = i,
                RawValue = rawValue,
                IsAvailable = isAvailable,
                FourByThreeStreamNumber = isAvailable ? (int)((rawValue >> 24) & 0x1F) : null,
                WideStreamNumber = isAvailable ? (int)((rawValue >> 16) & 0x1F) : null,
                LetterboxStreamNumber = isAvailable ? (int)((rawValue >> 8) & 0x1F) : null,
                PanScanStreamNumber = isAvailable ? (int)(rawValue & 0x1F) : null
            });
        }

        return controls;
    }

    private IReadOnlyList<CellPlaybackInfo> ParseCellPlaybackTable(ReadOnlySpan<byte> pgc, long pgcOffset, int offset, int count)
    {
        if (offset == 0 || count == 0)
        {
            return Array.Empty<CellPlaybackInfo>();
        }

        int byteCount = count * CellPlaybackRecordSize;
        if (!HasRange(pgc, offset, byteCount))
        {
            diagnostics.Error(pgcOffset + offset, "VD047", "PGC cell playback table is truncated");
            return Array.Empty<CellPlaybackInfo>();
        }

        var cells = new List<CellPlaybackInfo>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = pgc.Slice(offset + (i * CellPlaybackRecordSize), CellPlaybackRecordSize);
            byte firstFlagByte = entry[0];
            cells.Add(new CellPlaybackInfo
            {
                Number = i + 1,
                BlockMode = (DvdCellBlockMode)(firstFlagByte & 0x03),
                BlockType = ToBlockType((firstFlagByte >> 2) & 0x03),
                IsSeamlessPlayback = (firstFlagByte & 0x10) != 0,
                IsInterleaved = (firstFlagByte & 0x20) != 0,
                HasStcDiscontinuity = (firstFlagByte & 0x40) != 0,
                IsSeamlessAngle = (firstFlagByte & 0x80) != 0,
                HasStillTime = entry[1] != 0,
                StillTime = entry[1],
                CellCommandNumber = entry[2],
                PlaybackTime = ParsePlaybackTime(entry.Slice(4, 4)),
                FirstSector = BinaryPrimitives.ReadUInt32BigEndian(entry[8..12]),
                FirstInterleavedUnitEndSector = BinaryPrimitives.ReadUInt32BigEndian(entry[12..16]),
                LastVobuStartSector = BinaryPrimitives.ReadUInt32BigEndian(entry[16..20]),
                LastSector = BinaryPrimitives.ReadUInt32BigEndian(entry[20..24])
            });
        }

        return cells;
    }

    private IReadOnlyList<CellPositionInfo> ParseCellPositionTable(ReadOnlySpan<byte> pgc, long pgcOffset, int offset, int count)
    {
        if (offset == 0 || count == 0)
        {
            return Array.Empty<CellPositionInfo>();
        }

        int byteCount = count * CellPositionRecordSize;
        if (!HasRange(pgc, offset, byteCount))
        {
            diagnostics.Error(pgcOffset + offset, "VD048", "PGC cell position table is truncated");
            return Array.Empty<CellPositionInfo>();
        }

        var cells = new List<CellPositionInfo>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = pgc.Slice(offset + (i * CellPositionRecordSize), CellPositionRecordSize);
            cells.Add(new CellPositionInfo
            {
                Number = i + 1,
                VobId = BinaryPrimitives.ReadUInt16BigEndian(entry[..2]),
                CellId = entry[3]
            });
        }

        return cells;
    }

    private async ValueTask<List<VideoStreamInfo>> ParseVideoStreamsAsync()
    {
        var videos = new List<VideoStreamInfo>();
        const int videoAttributesOffset = 0x0200;

        ReadOnlyMemory<byte> videoData = await ReadBytesAtAsync(videoAttributesOffset, 2, "VTS video attributes");
        if (videoData.Length < 2)
        {
            return videos;
        }

        // VTS_V_ATR (DVD-Video; libdvdread video_attr_t):
        // byte 0 = MPEG version(7-6) | TV standard(5-4, 0=NTSC 1=PAL) | aspect ratio(3-2, 0=4:3 3=16:9) | permitted display(1-0);
        // byte 1 = line-21 CC field 1(7) | field 2(6) | unknown(5) | bit rate(4) | picture size(3-2) | letterboxed(1) | film mode(0).
        byte byte0 = videoData.Span[0];
        byte byte1 = videoData.Span[1];
        byte standardCode = (byte)((byte0 >> 4) & 0x03);
        bool isPal = standardCode == 1;
        byte aspectCode = (byte)((byte0 >> 2) & 0x03);
        byte resolutionCode = (byte)((byte1 >> 2) & 0x03);

        videos.Add(new VideoStreamInfo
        {
            Index = 0,
            Codec = ((byte0 >> 6) & 0x03) == 0 ? "MPEG-1" : "MPEG-2",
            AspectRatio = aspectCode == 3 ? "16:9" : "4:3",
            Resolution = GetDvdResolution(isPal, resolutionCode),
            IsPal = isPal,
            FrameRate = isPal ? 25.0 : 29.97,
            HasLine21ClosedCaptionField1 = (byte1 & 0x80) != 0,
            HasLine21ClosedCaptionField2 = (byte1 & 0x40) != 0,
        });

        return videos;
    }

    private async ValueTask<List<AudioStreamInfo>> ParseAudioStreamsAsync()
    {
        var audios = new List<AudioStreamInfo>();
        const int audioCountOffset = 0x0203;
        const int audioAttributesOffset = 0x0204;
        const int audioEntrySize = 8;
        const int maxAudioStreams = 8;

        int audioCount = Math.Min((int)await ReadByteAtAsync(audioCountOffset), maxAudioStreams);
        for (int i = 0; i < audioCount; i++)
        {
            ReadOnlyMemory<byte> audioData = await ReadBytesAtAsync(audioAttributesOffset + (i * audioEntrySize), audioEntrySize, "VTS audio attributes");
            if (audioData.Length < audioEntrySize)
            {
                break;
            }

            byte byte0 = audioData.Span[0];
            byte audioMode = (byte)((byte0 >> 5) & 0x07);
            audios.Add(new AudioStreamInfo
            {
                Index = i,
                Codec = audioMode switch
                {
                    0 => "AC-3",
                    1 => "MPEG-1 Layer 2",
                    2 => "MPEG-2",
                    3 => "LPCM",
                    4 => "DTS",
                    5 => "SDDS",
                    _ => "Unknown"
                },
                Channels = GetAudioChannelDescription((byte)(audioData.Span[1] & 0x07)),
                SamplingFrequency = 48000,
                LanguageCode = ReadIso639(audioData.Span.Slice(2, 2)),
                HasContentType = audioData.Span[5] != 0,
                ContentType = audioData.Span[5] == 0 ? null : $"0x{audioData.Span[5]:X2}"
            });
        }

        return audios;
    }

    private async ValueTask<List<SubtitleStreamInfo>> ParseSubtitleStreamsAsync()
    {
        var subtitles = new List<SubtitleStreamInfo>();
        const int subtitleCountOffset = 0x0255;
        const int subtitleAttributesOffset = 0x0256;
        const int subtitleEntrySize = 6;
        const int maxSubtitleStreams = 32;

        int subtitleCount = Math.Min((int)await ReadByteAtAsync(subtitleCountOffset), maxSubtitleStreams);
        for (int i = 0; i < subtitleCount; i++)
        {
            ReadOnlyMemory<byte> subData = await ReadBytesAtAsync(subtitleAttributesOffset + (i * subtitleEntrySize), subtitleEntrySize, "VTS subtitle attributes");
            if (subData.Length < subtitleEntrySize)
            {
                break;
            }

            // SP_ATR byte 0: coding mode(7-5, 0=2-bit RLE) | reserved(4-2) | language type(1-0).
            byte codingMode = (byte)((subData.Span[0] >> 5) & 0x07);
            subtitles.Add(new SubtitleStreamInfo
            {
                Index = i,
                CodingMode = codingMode switch
                {
                    0 => "RLE",
                    1 => "Extended",
                    2 => "Other",
                    _ => "Unknown"
                },
                LanguageCode = ReadIso639(subData.Span.Slice(2, 2)),
                HasContentType = subData.Span[5] != 0,
                ContentType = subData.Span[5] == 0 ? null : $"0x{subData.Span[5]:X2}"
            });
        }

        return subtitles;
    }

    private async ValueTask<string?> ReadStringAtAsync(long offset, int length)
    {
        ReadOnlyMemory<byte> bytes = await ReadBytesAtAsync(offset, length, "string");
        if (bytes.IsEmpty)
        {
            return null;
        }

        int nullIndex = bytes.Span.IndexOf((byte)0);
        ReadOnlySpan<byte> text = nullIndex >= 0 ? bytes.Span[..nullIndex] : bytes.Span;
        return Encoding.ASCII.GetString(text).TrimEnd();
    }

    private async ValueTask<ushort> ReadSpecificationVersionAsync()
    {
        byte encodedVersion = await ReadByteAtAsync(0x21);
        if (encodedVersion == 0)
        {
            diagnostics.Warning(0x21, "VD003", "Could not read specification version");
            return 0x0100;
        }

        return (ushort)((((encodedVersion >> 4) & 0x0F) << 8) | ((encodedVersion & 0x0F) << 4));
    }

    private async ValueTask<byte> ReadByteAtAsync(long offset)
    {
        reader.Seek(offset);
        return await reader.ReadByteAsync();
    }

    private async ValueTask<ushort> ReadUInt16AtAsync(long offset)
    {
        reader.Seek(offset);
        return await reader.ReadUInt16BigEndianAsync();
    }

    private async ValueTask<uint> ReadUInt32AtAsync(long offset)
    {
        reader.Seek(offset);
        return await reader.ReadUInt32BigEndianAsync();
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadBytesAtAsync(long offset, int length, string description)
    {
        reader.Seek(offset);
        ReadOnlyMemory<byte> data = await reader.ReadBytesAsync(length);
        if (data.Length < length)
        {
            diagnostics.Warning(offset, "VD099", $"{description} is truncated; expected {length} bytes, read {data.Length}");
        }

        return data;
    }

    private static IReadOnlyList<TitleSetReference> CreateTitleSetReferences(IReadOnlyList<VmgiTitle> titles, int numberOfTitleSets)
    {
        return titles
            .Where(item => item.TitleSetNumber > 0)
            .GroupBy(item => item.TitleSetNumber)
            .OrderBy(group => group.Key)
            .Take(numberOfTitleSets)
            .Select(group => new TitleSetReference
            {
                Number = group.Key,
                StartSectorAddress = group.First().TitleSetSector,
                LastSectorAddress = 0
            })
            .ToArray();
    }

    private static DvdTitlePlaybackFlags CreateTitlePlaybackFlags(byte value)
    {
        return new DvdTitlePlaybackFlags
        {
            RawValue = value,
            IsMultiOrRandomPgcTitle = (value & 0x02) != 0,
            HasJumpLinkCallInCellCommand = (value & 0x04) != 0,
            HasJumpLinkCallInPrePostCommand = (value & 0x08) != 0,
            HasJumpLinkCallInButtonCommand = (value & 0x10) != 0,
            HasJumpLinkCallInTitleDomain = (value & 0x20) != 0,
            ChapterSearchOrPlay = (value & 0x40) != 0,
            TitleOrTimePlay = (value & 0x80) != 0
        };
    }

    private static DvdPlaybackTime ParsePlaybackTime(ReadOnlySpan<byte> data)
    {
        byte frameByte = data[3];
        return new DvdPlaybackTime
        {
            Hours = DecodeBcd(data[0]),
            Minutes = DecodeBcd(data[1]),
            Seconds = DecodeBcd(data[2]),
            Frames = DecodeBcd((byte)(frameByte & 0x3F)),
            IsPal = (frameByte & 0xC0) == 0x40
        };
    }

    private static int DecodeBcd(byte value)
    {
        return (((value >> 4) & 0x0F) * 10) + (value & 0x0F);
    }

    private static string ReadIso639(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || (bytes[0] == 0 && bytes[1] == 0) || (bytes[0] == 0xFF && bytes[1] == 0xFF))
        {
            return "und";
        }

        return Encoding.ASCII.GetString(bytes);
    }

    private static DvdCellBlockType ToBlockType(int value)
    {
        return value switch
        {
            0 => DvdCellBlockType.None,
            1 => DvdCellBlockType.AngleBlock,
            _ => DvdCellBlockType.Reserved
        };
    }

    private static bool HasRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        return offset >= 0
            && length >= 0
            && offset <= data.Length
            && length <= data.Length - offset;
    }

    private bool ValidateRelativeRange(
        ReadOnlySpan<byte> table,
        int start,
        int end,
        int minimumStart,
        long tableOffset,
        string tableName,
        int itemNumber)
    {
        if (start < minimumStart)
        {
            diagnostics.Error(tableOffset + start, "VD050", $"{tableName} item {itemNumber} points inside the table header");
            return false;
        }

        if (end < start)
        {
            diagnostics.Error(tableOffset + start, "VD051", $"{tableName} item {itemNumber} has an inverted range");
            return false;
        }

        if (end > table.Length)
        {
            diagnostics.Error(tableOffset + start, "VD052", $"{tableName} item {itemNumber} extends past the parsed table");
            return false;
        }

        return true;
    }

    private int GetTableLength(uint lastByte, int minimumLength, string tableName, long tableOffset)
    {
        ulong declaredLength = (ulong)lastByte + 1UL;
        if (declaredLength > int.MaxValue)
        {
            diagnostics.Error(tableOffset, "VD053", $"{tableName} declares an unsupported length of {declaredLength} bytes");
            return minimumLength;
        }

        if (declaredLength < (ulong)minimumLength)
        {
            diagnostics.Error(tableOffset, "VD054", $"{tableName} declares {declaredLength} bytes but needs at least {minimumLength}");
            return minimumLength;
        }

        return (int)declaredLength;
    }

    private static long SectorToByteOffset(uint sector)
    {
        return sector * (long)SectorSize;
    }

    private static string GetDvdResolution(bool isPal, byte pictureSize)
    {
        return (isPal, pictureSize) switch
        {
            (true, 0) => "720x576",
            (true, 1) => "704x576",
            (true, 2) => "352x576",
            (true, 3) => "352x288",
            (false, 0) => "720x480",
            (false, 1) => "704x480",
            (false, 2) => "352x480",
            (false, 3) => "352x240",
            _ => isPal ? "720x576" : "720x480"
        };
    }

    private static string GetAudioChannelDescription(byte channels)
    {
        return channels switch
        {
            0 => "mono",
            1 => "stereo",
            5 => "5.1",
            _ => $"{channels + 1} channels"
        };
    }
}
