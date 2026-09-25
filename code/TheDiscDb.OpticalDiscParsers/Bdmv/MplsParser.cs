namespace TheDiscDb.OpticalDiscParsers.Bdmv;

using System.Buffers.Binary;
using System.Text;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Input;
using TheDiscDb.OpticalDiscParsers.Models;

/// <summary>
/// Parses Blu-ray playlist (.mpls) files.
/// </summary>
public sealed class MplsParser
{
    private readonly OpticalDiscBinaryReader reader;
    private readonly DiagnosticBag diagnostics;

    /// <summary>
    /// Creates a new MPLS parser.
    /// </summary>
    public MplsParser(OpticalDiscBinaryReader reader)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        diagnostics = new DiagnosticBag();
    }

    /// <summary>
    /// Parses an .mpls playlist file.
    /// </summary>
    public async ValueTask<ParserResult<MplsPlaylist>> ParseAsync()
    {
        diagnostics.Clear();
        reader.Diagnostics.Clear();

        var identifier = await ReadStringAtAsync(0, 4);
        var version = await ReadStringAtAsync(4, 4);
        if (identifier != "MPLS")
        {
            diagnostics.Error(0, "MP001", $"Expected MPLS identifier 'MPLS', got '{identifier ?? "<null>"}'");
            return new ParserResult<MplsPlaylist>(null, diagnostics.Diagnostics);
        }

        if (version is null)
        {
            diagnostics.Error(4, "MP002", "Missing MPLS version");
            return new ParserResult<MplsPlaylist>(null, diagnostics.Diagnostics);
        }

        uint playlistStartAddress = await ReadUInt32AtAsync(8);
        uint playlistMarkStartAddress = await ReadUInt32AtAsync(12);
        uint extensionDataStartAddress = await ReadUInt32AtAsync(16);
        var appInfo = await ParseAppInfoAsync();
        var (playItems, subPaths) = await ParsePlaylistSectionAsync(playlistStartAddress);
        var marks = await ParsePlaylistMarksAsync(playlistMarkStartAddress);
        var extensionParseResult = await ParseExtensionDataAsync(extensionDataStartAddress, playItems);
        var parserDiagnostics = diagnostics.Diagnostics.Concat(reader.Diagnostics.Diagnostics).ToArray();

        var playlist = new MplsPlaylist
        {
            Identifier = identifier,
            Version = version,
            PlaylistStartAddress = playlistStartAddress,
            PlaylistMarkStartAddress = playlistMarkStartAddress,
            ExtensionDataStartAddress = extensionDataStartAddress,
            AppInfo = appInfo,
            PlayItems = playItems,
            SubPaths = subPaths,
            Marks = marks,
            ExtensionData = extensionParseResult.ExtensionData,
            ExtensionSubPaths = extensionParseResult.ExtensionSubPaths,
            StereoVideoRelationships = extensionParseResult.StereoVideoRelationships,
            Diagnostics = parserDiagnostics
        };

        return new ParserResult<MplsPlaylist>(playlist, parserDiagnostics);
    }

    private async ValueTask<MplsAppInfo> ParseAppInfoAsync()
    {
        reader.Seek(40);
        uint length = await reader.ReadUInt32BigEndianAsync();
        _ = await reader.ReadByteAsync();
        byte playbackType = await reader.ReadByteAsync();
        ushort playbackCountOrReserved = await reader.ReadUInt16BigEndianAsync();
        _ = await reader.ReadBytesAsync(8);
        byte flags = await reader.ReadByteAsync();

        return new MplsAppInfo
        {
            Length = length,
            PlaybackTypeCode = playbackType,
            PlaybackType = GetPlaybackTypeName(playbackType),
            PlaybackCount = playbackType is 2 or 3 ? playbackCountOrReserved : null,
            RandomAccessFlag = (flags & 0x80) != 0,
            AudioMixFlag = (flags & 0x40) != 0,
            LosslessBypassFlag = (flags & 0x20) != 0,
            MvcBaseViewRFlag = (flags & 0x10) != 0
        };
    }

    private async ValueTask<(IReadOnlyList<MplsPlayItem> PlayItems, IReadOnlyList<MplsSubPath> SubPaths)> ParsePlaylistSectionAsync(uint playlistStartAddress)
    {
        if (playlistStartAddress == 0)
        {
            diagnostics.Warning(8, "MP003", "MPLS PlayList start address is 0");
            return (Array.Empty<MplsPlayItem>(), Array.Empty<MplsSubPath>());
        }

        reader.Seek(playlistStartAddress);
        uint length = await reader.ReadUInt32BigEndianAsync();
        _ = await reader.ReadUInt16BigEndianAsync();
        ushort playItemCount = await reader.ReadUInt16BigEndianAsync();
        ushort subPathCount = await reader.ReadUInt16BigEndianAsync();
        var playItems = new List<MplsPlayItem>(playItemCount);

        for (int i = 0; i < playItemCount; i++)
        {
            var playItem = await ParsePlayItemAsync(i);
            if (playItem is null)
            {
                break;
            }

            playItems.Add(playItem);
        }

        var subPaths = new List<MplsSubPath>(subPathCount);
        for (int i = 0; i < subPathCount; i++)
        {
            var subPath = await ParseSubPathAsync(i);
            if (subPath is null)
            {
                break;
            }

            subPaths.Add(subPath);
        }

        if (length == 0 && (playItems.Count > 0 || subPaths.Count > 0))
        {
            diagnostics.Warning(playlistStartAddress, "MP004", "PlayList length is 0 but entries were parsed");
        }

        return (playItems, subPaths);
    }

    private async ValueTask<MplsPlayItem?> ParsePlayItemAsync(int index)
    {
        long itemStart = reader.Position;
        ushort length = await reader.ReadUInt16BigEndianAsync();
        long itemPayloadStart = reader.Position;
        if (length < 18)
        {
            diagnostics.Warning(itemStart, "MP005", $"PlayItem {index} has invalid length {length}");
            return null;
        }

        var data = await reader.ReadBytesAsync(length);
        if (data.Length < length)
        {
            diagnostics.Warning(itemStart, "MP006", $"PlayItem {index} is truncated");
            return null;
        }

        var span = data.Span;
        string clipId = DecodeAscii(data.Slice(0, 5));
        string codecId = DecodeAscii(data.Slice(5, 4));
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(span[9..11]);
        bool isMultiAngle = (flags & 0x10) != 0;
        int connectionCondition = flags & 0x0F;
        byte stcId = span[11];
        uint inTime = BinaryPrimitives.ReadUInt32BigEndian(span[12..16]);
        uint outTime = BinaryPrimitives.ReadUInt32BigEndian(span[16..20]);
        bool randomAccessFlag = (span[28] & 0x80) != 0;
        byte stillMode = span[29];
        ushort stillTime = BinaryPrimitives.ReadUInt16BigEndian(span[30..32]);

        var clips = new List<MplsClipReference>
        {
            new()
            {
                ClipId = clipId,
                CodecId = codecId,
                StcId = stcId
            }
        };

        int stnOffset = 32;
        if (isMultiAngle)
        {
            int angleCount = Math.Max(1, (int)span[32]);
            stnOffset = 34;
            for (int i = 1; i < angleCount && stnOffset + 10 <= span.Length; i++)
            {
                clips.Add(new MplsClipReference
                {
                    ClipId = DecodeAscii(data.Slice(stnOffset, 5)),
                    CodecId = DecodeAscii(data.Slice(stnOffset + 5, 4)),
                    StcId = span[stnOffset + 9]
                });
                stnOffset += 10;
            }
        }

        var streamTable = ParseStreamTable(data.Slice(stnOffset), itemPayloadStart + stnOffset);
        reader.Seek(itemPayloadStart + length);

        return new MplsPlayItem
        {
            Index = index,
            ClipId = clipId,
            CodecId = codecId,
            ConnectionCondition = connectionCondition,
            IsMultiAngle = isMultiAngle,
            StcId = stcId,
            InTime = inTime,
            OutTime = outTime,
            RandomAccessFlag = randomAccessFlag,
            StillMode = stillMode,
            StillTime = stillMode == 1 ? stillTime : null,
            Clips = clips,
            StreamTable = streamTable
        };
    }

    private MplsStreamTable ParseStreamTable(ReadOnlyMemory<byte> data, long absoluteOffset)
    {
        if (data.Length < 16)
        {
            diagnostics.Warning(absoluteOffset, "MP007", "STN table is truncated");
            return EmptyStreamTable();
        }

        var span = data.Span;
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(span[0..2]);
        int offset = 4;
        int videoCount = span[offset++];
        int audioCount = span[offset++];
        int pgCount = span[offset++];
        int igCount = span[offset++];
        int secondaryAudioCount = span[offset++];
        int secondaryVideoCount = span[offset++];
        int pipPgCount = span[offset++];
        offset += 5;

        var video = ParseStreams(data, ref offset, videoCount, "Video");
        var audio = ParseStreams(data, ref offset, audioCount, "Audio");
        var pg = ParseStreams(data, ref offset, pgCount + pipPgCount, "PresentationGraphics");
        var ig = ParseStreams(data, ref offset, igCount, "InteractiveGraphics");
        var secondaryAudio = ParseStreams(data, ref offset, secondaryAudioCount, "SecondaryAudio");
        var secondaryVideo = ParseStreams(data, ref offset, secondaryVideoCount, "SecondaryVideo");

        if (length > data.Length - 2)
        {
            diagnostics.Warning(absoluteOffset, "MP008", $"STN table length {length} exceeds available bytes {data.Length - 2}");
        }

        return new MplsStreamTable
        {
            VideoStreams = video,
            AudioStreams = audio,
            PresentationGraphicsStreams = pg,
            InteractiveGraphicsStreams = ig,
            SecondaryAudioStreams = secondaryAudio,
            SecondaryVideoStreams = secondaryVideo
        };
    }

    private List<MplsStream> ParseStreams(ReadOnlyMemory<byte> data, ref int offset, int count, string category)
    {
        var streams = new List<MplsStream>(count);
        for (int i = 0; i < count; i++)
        {
            var stream = ParseStream(data, ref offset, category);
            if (stream is null)
            {
                break;
            }

            streams.Add(stream);

            if (category == "SecondaryAudio" && offset + 2 <= data.Length)
            {
                int references = data.Span[offset];
                offset += 2 + references + (references % 2);
            }
            else if (category == "SecondaryVideo" && offset + 2 <= data.Length)
            {
                int audioReferences = data.Span[offset];
                offset += 2 + audioReferences + (audioReferences % 2);
                if (offset + 2 <= data.Length)
                {
                    int pipReferences = data.Span[offset];
                    offset += 2 + pipReferences + (pipReferences % 2);
                }
            }
        }

        return streams;
    }

    private MplsStream? ParseStream(ReadOnlyMemory<byte> data, ref int offset, string category)
    {
        if (offset >= data.Length)
        {
            return null;
        }

        int streamLength = data.Span[offset++];
        if (offset + streamLength > data.Length)
        {
            diagnostics.Warning(offset, "MP009", "Stream entry is truncated");
            return null;
        }

        var streamSpan = data.Span.Slice(offset, streamLength);
        offset += streamLength;

        int streamType = streamSpan[0];
        ushort? pid = null;
        int? subPathId = null;
        int? subClipId = null;
        if (streamType == 1 && streamSpan.Length >= 3)
        {
            pid = BinaryPrimitives.ReadUInt16BigEndian(streamSpan[1..3]);
        }
        else if ((streamType == 2 || streamType == 4) && streamSpan.Length >= 5)
        {
            subPathId = streamSpan[1];
            subClipId = streamSpan[2];
            pid = BinaryPrimitives.ReadUInt16BigEndian(streamSpan[3..5]);
        }
        else if (streamType == 3 && streamSpan.Length >= 4)
        {
            subPathId = streamSpan[1];
            pid = BinaryPrimitives.ReadUInt16BigEndian(streamSpan[2..4]);
        }

        if (offset >= data.Length)
        {
            return null;
        }

        int attrLength = data.Span[offset++];
        if (offset + attrLength > data.Length)
        {
            diagnostics.Warning(offset, "MP010", "Stream attribute entry is truncated");
            return null;
        }

        var attr = data.Span.Slice(offset, attrLength);
        offset += attrLength;
        byte codingType = attr.Length > 0 ? attr[0] : (byte)0;
        int? formatCode = null;
        int? rateCode = null;
        string? languageCode = null;
        int? characterCode = null;

        if (IsVideoCodingType(codingType) && attr.Length >= 2)
        {
            formatCode = (attr[1] >> 4) & 0x0F;
            rateCode = attr[1] & 0x0F;
        }
        else if (IsAudioCodingType(codingType) && attr.Length >= 5)
        {
            formatCode = (attr[1] >> 4) & 0x0F;
            rateCode = attr[1] & 0x0F;
            languageCode = DecodeAscii(data.Slice(offset - attrLength + 2, 3));
        }
        else if (codingType is 0x90 or 0x91 && attr.Length >= 4)
        {
            languageCode = DecodeAscii(data.Slice(offset - attrLength + 1, 3));
        }
        else if (codingType == 0x92 && attr.Length >= 5)
        {
            characterCode = attr[1];
            languageCode = DecodeAscii(data.Slice(offset - attrLength + 2, 3));
        }

        return new MplsStream
        {
            Category = category,
            StreamTypeCode = streamType,
            Pid = pid,
            SubPathId = subPathId,
            SubClipId = subClipId,
            CodingTypeCode = codingType,
            CodingType = GetCodingTypeName(codingType),
            FormatCode = formatCode,
            RateCode = rateCode,
            LanguageCode = languageCode,
            CharacterCode = characterCode
        };
    }

    private async ValueTask<MplsSubPath?> ParseSubPathAsync(int index)
    {
        long start = reader.Position;
        uint length = await reader.ReadUInt32BigEndianAsync();
        if (length < 6)
        {
            diagnostics.Warning(start, "MP011", $"SubPath {index} has invalid length {length}");
            return null;
        }

        ReadOnlyMemory<byte> data = await reader.ReadBytesAsync((int)length);
        if (data.Length < length)
        {
            diagnostics.Warning(start, "MP013", $"SubPath {index} is truncated");
            return null;
        }

        int offset = 0;
        var subPath = ParseSubPathPayload(data, ref offset, index, start + 4);
        reader.Seek(start + 4 + length);
        return subPath;
    }

    private MplsSubPath? ParseSubPathPayload(ReadOnlyMemory<byte> data, ref int offset, int index, long absoluteOffset)
    {
        if (offset + 6 > data.Length)
        {
            diagnostics.Warning(absoluteOffset + offset, "MP026", $"SubPath {index} payload is truncated");
            return null;
        }

        int payloadOffset = offset;
        ReadOnlySpan<byte> span = data.Span;
        byte type = span[payloadOffset + 1];
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(payloadOffset + 2, 2));
        byte count = span[payloadOffset + 5];
        offset = payloadOffset + 6;
        var subPlayItems = new List<MplsSubPlayItem>(count);
        for (int i = 0; i < count; i++)
        {
            var subPlayItem = ParseSubPlayItem(data, ref offset, i, absoluteOffset);
            if (subPlayItem is null)
            {
                break;
            }

            subPlayItems.Add(subPlayItem);
        }

        return new MplsSubPath
        {
            Index = index,
            Type = type,
            IsRepeat = (flags & 1) != 0,
            SubPlayItemCount = count,
            SubPlayItems = subPlayItems
        };
    }

    private async ValueTask<MplsExtensionParseResult> ParseExtensionDataAsync(
        uint extensionDataStartAddress,
        IReadOnlyList<MplsPlayItem> playItems)
    {
        if (extensionDataStartAddress == 0)
        {
            return MplsExtensionParseResult.Empty;
        }

        var header = await ReadBytesAtAsync(extensionDataStartAddress, 12);
        if (header.Length < 12)
        {
            diagnostics.Warning(extensionDataStartAddress, "MP017", "MPLS ExtensionData header is truncated");
            return MplsExtensionParseResult.Empty;
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header.Span[0..4]);
        if (length == 0)
        {
            return MplsExtensionParseResult.Empty;
        }

        if (length > int.MaxValue - 4)
        {
            diagnostics.Warning(extensionDataStartAddress, "MP018", $"MPLS ExtensionData length {length} is too large to parse");
            return MplsExtensionParseResult.Empty;
        }

        ReadOnlyMemory<byte> block = await ReadBytesAtAsync(extensionDataStartAddress, checked((int)length + 4));
        if (block.Length < length + 4)
        {
            diagnostics.Warning(extensionDataStartAddress, "MP019", $"MPLS ExtensionData is truncated: expected {length + 4} bytes, got {block.Length}");
        }

        if (block.Length < 12)
        {
            return MplsExtensionParseResult.Empty;
        }

        uint dataBlockStartAddress = BinaryPrimitives.ReadUInt32BigEndian(block.Span[4..8]);
        int entryCount = block.Span[11];
        int descriptorEnd = 12 + (entryCount * 12);
        if (descriptorEnd > block.Length || descriptorEnd > length + 4)
        {
            diagnostics.Warning(extensionDataStartAddress + 12, "MP020", $"MPLS ExtensionData descriptor table for {entryCount} entries is truncated");
            entryCount = Math.Max(0, (Math.Min(block.Length, checked((int)length + 4)) - 12) / 12);
        }

        var descriptors = new List<ExtensionDescriptor>(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            int descriptorOffset = 12 + (i * 12);
            ReadOnlySpan<byte> descriptor = block.Span.Slice(descriptorOffset, 12);
            descriptors.Add(new ExtensionDescriptor(
                i,
                BinaryPrimitives.ReadUInt16BigEndian(descriptor[0..2]),
                BinaryPrimitives.ReadUInt16BigEndian(descriptor[2..4]),
                BinaryPrimitives.ReadUInt32BigEndian(descriptor[4..8]),
                BinaryPrimitives.ReadUInt32BigEndian(descriptor[8..12])));
        }

        var ranges = descriptors
            .Select(item => new ExtensionRange(item.Index, item.RelativeStartAddress, (ulong)item.RelativeStartAddress + item.Length))
            .ToArray();
        var extensionEntries = new List<MplsExtensionDataEntry>(descriptors.Count);
        var ssStreams = new List<MplsStereoVideoStream>();
        var extensionSubPaths = new List<MplsSubPath>();

        foreach (var descriptor in descriptors)
        {
            bool overlaps = ranges.Any(item =>
                item.Index != descriptor.Index
                && descriptor.RelativeStartAddress < item.End
                && (ulong)descriptor.RelativeStartAddress + descriptor.Length > item.Start);
            if (overlaps)
            {
                diagnostics.Warning(extensionDataStartAddress + 12 + (descriptor.Index * 12), "MP021", $"MPLS ExtensionData entry {descriptor.Index} overlaps another entry");
            }

            ulong entryEnd = (ulong)descriptor.RelativeStartAddress + descriptor.Length;
            bool entryInBounds = descriptor.RelativeStartAddress <= block.Length
                && descriptor.Length <= block.Length - descriptor.RelativeStartAddress
                && entryEnd <= length + 4;
            if (!entryInBounds)
            {
                diagnostics.Warning(extensionDataStartAddress + 12 + (descriptor.Index * 12), "MP022", $"MPLS ExtensionData entry {descriptor.Index} extends past the declared extension block");
                extensionEntries.Add(CreateExtensionEntry(descriptor, false, overlaps, null, ReadOnlyMemory<byte>.Empty));
                continue;
            }

            ReadOnlyMemory<byte> entryData = block.Slice((int)descriptor.RelativeStartAddress, (int)descriptor.Length);
            bool supported = true;
            string? name = GetExtensionName(descriptor.TypeIdentifier, descriptor.VersionIdentifier);
            if (descriptor.TypeIdentifier == 2 && descriptor.VersionIdentifier == 1)
            {
                ssStreams.AddRange(ParseStnSsExtension(entryData, playItems, extensionDataStartAddress + descriptor.RelativeStartAddress));
            }
            else if (descriptor.TypeIdentifier == 2 && descriptor.VersionIdentifier == 2)
            {
                extensionSubPaths.AddRange(ParseExtensionSubPaths(entryData, extensionDataStartAddress + descriptor.RelativeStartAddress));
            }
            else
            {
                supported = false;
                diagnostics.Info(extensionDataStartAddress + descriptor.RelativeStartAddress, "MP023", $"Unsupported MPLS ExtensionData entry type {descriptor.TypeIdentifier}.{descriptor.VersionIdentifier} was skipped");
            }

            extensionEntries.Add(CreateExtensionEntry(descriptor, supported, overlaps, name, supported ? ReadOnlyMemory<byte>.Empty : entryData));
        }

        var extensionData = new MplsExtensionData
        {
            Length = length,
            DataBlockStartAddress = dataBlockStartAddress,
            Entries = extensionEntries
        };
        var relationships = CreateStereoVideoRelationships(playItems, extensionSubPaths, ssStreams);
        return new MplsExtensionParseResult(extensionData, extensionSubPaths, relationships);
    }

    private IReadOnlyList<MplsSubPath> ParseExtensionSubPaths(ReadOnlyMemory<byte> data, long absoluteOffset)
    {
        if (data.Length < 6)
        {
            diagnostics.Warning(absoluteOffset, "MP024", "MPLS SubPath extension is truncated");
            return Array.Empty<MplsSubPath>();
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(data.Span[0..4]);
        ushort subPathCount = BinaryPrimitives.ReadUInt16BigEndian(data.Span[4..6]);
        if (length > data.Length - 4)
        {
            diagnostics.Warning(absoluteOffset, "MP025", $"MPLS SubPath extension length {length} exceeds available bytes {data.Length - 4}");
        }

        int offset = 6;
        var subPaths = new List<MplsSubPath>(subPathCount);
        for (int i = 0; i < subPathCount; i++)
        {
            if (offset + 4 > data.Length)
            {
                diagnostics.Warning(absoluteOffset + offset, "MP026", $"Extension SubPath {i} length is truncated");
                break;
            }

            uint subPathLength = BinaryPrimitives.ReadUInt32BigEndian(data.Span.Slice(offset, 4));
            int payloadOffset = offset + 4;
            if (subPathLength > data.Length - payloadOffset)
            {
                diagnostics.Warning(absoluteOffset + offset, "MP027", $"Extension SubPath {i} extends past the extension entry boundary");
                break;
            }

            int subPathPayloadOffset = 0;
            var subPath = ParseSubPathPayload(
                data.Slice(payloadOffset, (int)subPathLength),
                ref subPathPayloadOffset,
                i,
                absoluteOffset + payloadOffset);
            if (subPath is not null)
            {
                subPaths.Add(subPath);
            }

            offset = payloadOffset + (int)subPathLength;
        }

        return subPaths;
    }

    private IReadOnlyList<MplsStereoVideoStream> ParseStnSsExtension(
        ReadOnlyMemory<byte> data,
        IReadOnlyList<MplsPlayItem> playItems,
        long absoluteOffset)
    {
        int offset = 0;
        var streams = new List<MplsStereoVideoStream>();
        for (int playItemIndex = 0; playItemIndex < playItems.Count; playItemIndex++)
        {
            if (offset + 2 > data.Length)
            {
                diagnostics.Warning(absoluteOffset + offset, "MP028", $"STN SS extension for PlayItem {playItemIndex} is truncated");
                break;
            }

            ushort itemLength = BinaryPrimitives.ReadUInt16BigEndian(data.Span.Slice(offset, 2));
            int itemPayloadStart = offset + 2;
            int itemEnd = itemPayloadStart + itemLength;
            if (itemEnd > data.Length)
            {
                diagnostics.Warning(absoluteOffset + offset, "MP029", $"STN SS extension for PlayItem {playItemIndex} extends past its entry boundary");
                break;
            }

            int itemOffset = itemPayloadStart;
            if (itemOffset + 2 > itemEnd)
            {
                diagnostics.Warning(absoluteOffset + itemOffset, "MP030", $"STN SS extension flags for PlayItem {playItemIndex} are truncated");
                break;
            }

            itemOffset += 2;
            var videoStreams = playItems[playItemIndex].StreamTable.VideoStreams;
            for (int videoStreamIndex = 0; videoStreamIndex < videoStreams.Count; videoStreamIndex++)
            {
                var stereoStream = ParseStnSsVideoStream(data, ref itemOffset, itemEnd, playItemIndex, videoStreamIndex, absoluteOffset);
                if (stereoStream is not null)
                {
                    streams.Add(stereoStream);
                }
            }

            itemOffset = SkipStereoscopicGraphicsEntries(data, itemOffset, itemEnd, playItems[playItemIndex].StreamTable.PresentationGraphicsStreams.Count, absoluteOffset, "PG");
            itemOffset = SkipStereoscopicGraphicsEntries(data, itemOffset, itemEnd, playItems[playItemIndex].StreamTable.InteractiveGraphicsStreams.Count, absoluteOffset, "IG");
            offset = itemEnd;
        }

        return streams;
    }

    private MplsStereoVideoStream? ParseStnSsVideoStream(
        ReadOnlyMemory<byte> data,
        ref int offset,
        int itemEnd,
        int playItemIndex,
        int videoStreamIndex,
        long absoluteOffset)
    {
        var streamEntry = ReadLengthPrefixed(data, ref offset, itemEnd, absoluteOffset, $"STN SS stream entry {videoStreamIndex}");
        if (streamEntry is null)
        {
            return null;
        }

        var streamAttributes = ReadLengthPrefixed(data, ref offset, itemEnd, absoluteOffset, $"STN SS stream attributes {videoStreamIndex}");
        if (streamAttributes is null)
        {
            return null;
        }

        if (offset + 2 > itemEnd)
        {
            diagnostics.Warning(absoluteOffset + offset, "MP031", $"STN SS offset-sequence count for PlayItem {playItemIndex}, video stream {videoStreamIndex} is truncated");
            return null;
        }

        int numberOfOffsetSequences = data.Span[offset + 1] & 0x3F;
        offset += 2;

        var parsedEntry = ParseStreamEntry(streamEntry.Value);
        var parsedAttributes = ParseStreamAttributes(streamAttributes.Value);
        return new MplsStereoVideoStream
        {
            PlayItemIndex = playItemIndex,
            VideoStreamIndex = videoStreamIndex,
            StreamTypeCode = parsedEntry.StreamTypeCode,
            Pid = parsedEntry.Pid,
            SubPathId = parsedEntry.SubPathId,
            SubClipId = parsedEntry.SubClipId,
            CodingTypeCode = parsedAttributes.CodingTypeCode,
            CodingType = GetCodingTypeName(parsedAttributes.CodingTypeCode),
            FormatCode = parsedAttributes.FormatCode,
            RateCode = parsedAttributes.RateCode,
            NumberOfOffsetSequences = numberOfOffsetSequences,
            RawAttributesHex = Convert.ToHexString(streamAttributes.Value.Span)
        };
    }

    private int SkipStereoscopicGraphicsEntries(
        ReadOnlyMemory<byte> data,
        int offset,
        int itemEnd,
        int count,
        long absoluteOffset,
        string category)
    {
        for (int i = 0; i < count; i++)
        {
            if (offset + 2 > itemEnd)
            {
                diagnostics.Warning(absoluteOffset + offset, "MP032", $"STN SS {category} entry {i} is truncated");
                return itemEnd;
            }

            byte flags = data.Span[offset + 1];
            offset += 2;
            if ((flags & 0x04) != 0)
            {
                offset = SkipLengthPrefixed(data, offset, itemEnd, absoluteOffset, $"STN SS {category} left-eye stream entry {i}");
                offset = SkipLengthPrefixed(data, offset, itemEnd, absoluteOffset, $"STN SS {category} right-eye stream entry {i}");
                offset += Math.Min(2, itemEnd - offset);
            }

            if ((flags & 0x02) != 0)
            {
                offset = SkipLengthPrefixed(data, offset, itemEnd, absoluteOffset, $"STN SS {category} top stream entry {i}");
                offset += Math.Min(2, itemEnd - offset);
            }

            if ((flags & 0x01) != 0)
            {
                offset = SkipLengthPrefixed(data, offset, itemEnd, absoluteOffset, $"STN SS {category} bottom stream entry {i}");
                offset += Math.Min(2, itemEnd - offset);
            }
        }

        return offset;
    }

    private static IReadOnlyList<MplsStereoVideoRelationship> CreateStereoVideoRelationships(
        IReadOnlyList<MplsPlayItem> playItems,
        IReadOnlyList<MplsSubPath> extensionSubPaths,
        IReadOnlyList<MplsStereoVideoStream> ssStreams)
    {
        var relationships = new List<MplsStereoVideoRelationship>();
        foreach (var subPath in extensionSubPaths.Where(item => item.Type == 8))
        {
            foreach (var subPlayItem in subPath.SubPlayItems)
            {
                var baseItem = playItems.ElementAtOrDefault(subPlayItem.SyncPlayItemId);
                if (baseItem is null)
                {
                    continue;
                }

                var dependentClip = subPlayItem.Clips.FirstOrDefault()
                    ?? new MplsClipReference
                    {
                        ClipId = subPlayItem.ClipId,
                        CodecId = subPlayItem.CodecId,
                        StcId = subPlayItem.StcId
                    };
                var stream = ssStreams.FirstOrDefault(item =>
                    item.PlayItemIndex == baseItem.Index
                    && item.SubPathId == subPath.Index
                    && item.SubClipId == subPlayItem.Index);
                relationships.Add(new MplsStereoVideoRelationship
                {
                    RelationshipType = MplsStereoVideoRelationship.ThreeDimensionalDependentView,
                    BasePlayItemIndex = baseItem.Index,
                    BaseClipId = baseItem.ClipId,
                    BaseCodecId = baseItem.CodecId,
                    BaseStcId = baseItem.StcId,
                    DependentSubPathIndex = subPath.Index,
                    DependentSubPathType = subPath.Type,
                    DependentSubPlayItemIndex = subPlayItem.Index,
                    DependentClipId = dependentClip.ClipId,
                    DependentCodecId = dependentClip.CodecId,
                    DependentStcId = dependentClip.StcId,
                    SyncPlayItemId = subPlayItem.SyncPlayItemId,
                    SyncPresentationTimestamp = subPlayItem.SyncPresentationTimestamp,
                    IsSsVideoSubPath = true,
                    DependentViewStream = stream
                });
            }
        }

        return relationships;
    }

    private MplsExtensionDataEntry CreateExtensionEntry(
        ExtensionDescriptor descriptor,
        bool isSupported,
        bool overlaps,
        string? name,
        ReadOnlyMemory<byte> rawData)
    {
        const int maxRawDataBytes = 256;
        var rawSlice = rawData[..Math.Min(rawData.Length, maxRawDataBytes)];
        return new MplsExtensionDataEntry
        {
            Index = descriptor.Index,
            TypeIdentifier = descriptor.TypeIdentifier,
            VersionIdentifier = descriptor.VersionIdentifier,
            RelativeStartAddress = descriptor.RelativeStartAddress,
            Length = descriptor.Length,
            Name = name,
            IsSupported = isSupported,
            OverlapsAnotherEntry = overlaps,
            RawDataHex = rawSlice.IsEmpty ? null : Convert.ToHexString(rawSlice.Span),
            IsRawDataTruncated = rawData.Length > maxRawDataBytes
        };
    }

    private ReadOnlyMemory<byte>? ReadLengthPrefixed(
        ReadOnlyMemory<byte> data,
        ref int offset,
        int end,
        long absoluteOffset,
        string description)
    {
        if (offset >= end)
        {
            diagnostics.Warning(absoluteOffset + offset, "MP033", $"{description} length is truncated");
            return null;
        }

        int length = data.Span[offset];
        int payloadStart = offset + 1;
        if (length > end - payloadStart)
        {
            diagnostics.Warning(absoluteOffset + offset, "MP034", $"{description} extends past its entry boundary");
            return null;
        }

        offset = payloadStart + length;
        return data.Slice(payloadStart, length);
    }

    private int SkipLengthPrefixed(
        ReadOnlyMemory<byte> data,
        int offset,
        int end,
        long absoluteOffset,
        string description)
    {
        var currentOffset = offset;
        return ReadLengthPrefixed(data, ref currentOffset, end, absoluteOffset, description) is null
            ? end
            : currentOffset;
    }

    private static ParsedStreamEntry ParseStreamEntry(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            return new ParsedStreamEntry(0, null, null, null);
        }

        ReadOnlySpan<byte> span = data.Span;
        byte streamType = span[0];
        return streamType switch
        {
            1 when span.Length >= 3 => new ParsedStreamEntry(
                streamType,
                BinaryPrimitives.ReadUInt16BigEndian(span[1..3]),
                null,
                null),
            2 when span.Length >= 5 => new ParsedStreamEntry(
                streamType,
                BinaryPrimitives.ReadUInt16BigEndian(span[3..5]),
                span[1],
                span[2]),
            3 or 4 when span.Length >= 4 => new ParsedStreamEntry(
                streamType,
                BinaryPrimitives.ReadUInt16BigEndian(span[2..4]),
                span[1],
                null),
            _ => new ParsedStreamEntry(streamType, null, null, null)
        };
    }

    private static ParsedStreamAttributes ParseStreamAttributes(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            return new ParsedStreamAttributes(0, null, null);
        }

        byte codingType = data.Span[0];
        int? formatCode = null;
        int? rateCode = null;
        if (data.Length >= 2)
        {
            formatCode = (data.Span[1] >> 4) & 0x0F;
            rateCode = data.Span[1] & 0x0F;
        }

        return new ParsedStreamAttributes(codingType, formatCode, rateCode);
    }

    private MplsSubPlayItem? ParseSubPlayItem(ReadOnlyMemory<byte> data, ref int offset, int index, long subPathPayloadOffset)
    {
        if (offset + 2 > data.Length)
        {
            diagnostics.Warning(subPathPayloadOffset + offset, "MP014", $"SubPlayItem {index} length is truncated");
            return null;
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(data.Span.Slice(offset, 2));
        int payloadStart = offset + 2;
        if (length < 24)
        {
            diagnostics.Warning(subPathPayloadOffset + offset, "MP015", $"SubPlayItem {index} has invalid length {length}");
            return null;
        }

        if (payloadStart + length > data.Length)
        {
            diagnostics.Warning(subPathPayloadOffset + offset, "MP016", $"SubPlayItem {index} extends past the SubPath boundary");
            return null;
        }

        ReadOnlyMemory<byte> item = data.Slice(payloadStart, length);
        ReadOnlySpan<byte> span = item.Span;
        string clipId = DecodeAscii(item.Slice(0, 5));
        string codecId = DecodeAscii(item.Slice(5, 4));
        int connectionCondition = span.Length > 11 ? (span[11] >> 1) & 0x0F : 0;
        bool isMultiClip = span.Length > 11 && (span[11] & 0x01) != 0;
        byte stcId = span.Length > 12 ? span[12] : (byte)0;
        uint inTime = span.Length >= 18 ? BinaryPrimitives.ReadUInt32BigEndian(span[14..18]) : 0;
        uint outTime = span.Length >= 22 ? BinaryPrimitives.ReadUInt32BigEndian(span[18..22]) : 0;
        ushort syncPlayItemId = span.Length >= 24 ? BinaryPrimitives.ReadUInt16BigEndian(span[22..24]) : (ushort)0;
        uint syncPts = span.Length >= 28 ? BinaryPrimitives.ReadUInt32BigEndian(span[24..28]) : 0;

        var clips = new List<MplsClipReference>
        {
            new()
            {
                ClipId = clipId,
                CodecId = codecId,
                StcId = stcId
            }
        };

        int clipOffset = 28;
        int clipCount = 1;
        if (isMultiClip && span.Length > clipOffset)
        {
            clipCount = Math.Max(1, (int)span[clipOffset]);
            clipOffset++;
        }

        for (int i = 1; i < clipCount && clipOffset + 10 <= span.Length; i++)
        {
            clips.Add(new MplsClipReference
            {
                ClipId = DecodeAscii(item.Slice(clipOffset, 5)),
                CodecId = DecodeAscii(item.Slice(clipOffset + 5, 4)),
                StcId = span[clipOffset + 9]
            });
            clipOffset += 10;
        }

        offset = payloadStart + length;
        return new MplsSubPlayItem
        {
            Index = index,
            ClipId = clipId,
            CodecId = codecId,
            ConnectionCondition = connectionCondition,
            IsMultiClip = isMultiClip,
            StcId = stcId,
            InTime = inTime,
            OutTime = outTime,
            SyncPlayItemId = syncPlayItemId,
            SyncPresentationTimestamp = syncPts,
            Clips = clips
        };
    }

    private async ValueTask<IReadOnlyList<MplsPlaylistMark>> ParsePlaylistMarksAsync(uint playlistMarkStartAddress)
    {
        if (playlistMarkStartAddress == 0)
        {
            return Array.Empty<MplsPlaylistMark>();
        }

        reader.Seek(playlistMarkStartAddress);
        uint length = await reader.ReadUInt32BigEndianAsync();
        ushort markCount = await reader.ReadUInt16BigEndianAsync();
        var marks = new List<MplsPlaylistMark>(markCount);

        for (int i = 0; i < markCount; i++)
        {
            _ = await reader.ReadByteAsync();
            byte markType = await reader.ReadByteAsync();
            ushort playItemRef = await reader.ReadUInt16BigEndianAsync();
            uint time = await reader.ReadUInt32BigEndianAsync();
            ushort entryPid = await reader.ReadUInt16BigEndianAsync();
            uint duration = await reader.ReadUInt32BigEndianAsync();

            marks.Add(new MplsPlaylistMark
            {
                Index = i,
                MarkType = markType,
                PlayItemReference = playItemRef,
                Time = time,
                EntryElementaryStreamPid = entryPid,
                Duration = duration
            });
        }

        if (length == 0 && marks.Count > 0)
        {
            diagnostics.Warning(playlistMarkStartAddress, "MP012", "PlayListMark length is 0 but marks were parsed");
        }

        return marks;
    }

    private static MplsStreamTable EmptyStreamTable()
    {
        return new MplsStreamTable
        {
            VideoStreams = Array.Empty<MplsStream>(),
            AudioStreams = Array.Empty<MplsStream>(),
            PresentationGraphicsStreams = Array.Empty<MplsStream>(),
            InteractiveGraphicsStreams = Array.Empty<MplsStream>(),
            SecondaryAudioStreams = Array.Empty<MplsStream>(),
            SecondaryVideoStreams = Array.Empty<MplsStream>()
        };
    }

    private async ValueTask<string?> ReadStringAtAsync(long offset, int length)
    {
        var bytes = await ReadBytesAtAsync(offset, length);
        if (bytes.IsEmpty)
        {
            return null;
        }

        return DecodeAscii(bytes);
    }

    private async ValueTask<uint> ReadUInt32AtAsync(long offset)
    {
        var bytes = await ReadBytesAtAsync(offset, sizeof(uint));
        if (bytes.Length < sizeof(uint))
        {
            diagnostics.Warning(offset, "MP013", $"Expected {sizeof(uint)} bytes, got {bytes.Length}");
            return 0;
        }

        return BinaryPrimitives.ReadUInt32BigEndian(bytes.Span);
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadBytesAtAsync(long offset, int length)
    {
        reader.Seek(offset);
        return await reader.ReadBytesAsync(length);
    }

    private static string DecodeAscii(ReadOnlyMemory<byte> bytes)
    {
        int length = bytes.Length;
        while (length > 0 && bytes.Span[length - 1] == 0)
        {
            length--;
        }

        return Encoding.ASCII.GetString(bytes.Span[..length]);
    }

    private static string GetPlaybackTypeName(int playbackType)
    {
        return playbackType switch
        {
            1 => "Sequential",
            2 => "Random",
            3 => "Shuffle",
            _ => "Unknown"
        };
    }

    private static string? GetExtensionName(ushort typeIdentifier, ushort versionIdentifier)
        => (typeIdentifier, versionIdentifier) switch
        {
            (2, 1) => "STN SS extension",
            (2, 2) => "SubPath entries extension",
            (1, 1) => "PiP metadata extension",
            (3, 5) => "Static metadata extension",
            _ => null
        };

    private static bool IsVideoCodingType(byte codingType)
    {
        return codingType is 0x01 or 0x02 or 0xEA or 0x1B or 0x20 or 0x24;
    }

    private static bool IsAudioCodingType(byte codingType)
    {
        return codingType is 0x03 or 0x04 or 0x80 or 0x81 or 0x82 or 0x83 or 0x84 or 0x85 or 0x86 or 0xA1 or 0xA2;
    }

    private static string GetCodingTypeName(byte codingType)
    {
        return codingType switch
        {
            0x01 => "MPEG-1 Video",
            0x02 => "MPEG-2 Video",
            0x03 => "MPEG-1 Audio",
            0x04 => "MPEG-2 Audio",
            0x1B => "AVC Video",
            0x20 => "MVC Video",
            0x24 => "HEVC Video",
            0x80 => "LPCM",
            0x81 => "AC-3",
            0x82 => "DTS",
            0x83 => "TrueHD",
            0x84 => "AC-3 Plus",
            0x85 => "DTS-HD High Resolution",
            0x86 => "DTS-HD Master Audio",
            0x90 => "Presentation Graphics",
            0x91 => "Interactive Graphics",
            0x92 => "Text Subtitle",
            0xA1 => "AC-3 Plus Secondary Audio",
            0xA2 => "DTS-HD Secondary Audio",
            0xEA => "VC-1 Video",
            _ => "Unknown"
        };
    }

    private sealed record ExtensionDescriptor(
        int Index,
        ushort TypeIdentifier,
        ushort VersionIdentifier,
        uint RelativeStartAddress,
        uint Length);

    private sealed record ExtensionRange(int Index, ulong Start, ulong End);

    private sealed record MplsExtensionParseResult(
        MplsExtensionData? ExtensionData,
        IReadOnlyList<MplsSubPath> ExtensionSubPaths,
        IReadOnlyList<MplsStereoVideoRelationship> StereoVideoRelationships)
    {
        public static MplsExtensionParseResult Empty { get; } = new(
            null,
            Array.Empty<MplsSubPath>(),
            Array.Empty<MplsStereoVideoRelationship>());
    }

    private sealed record ParsedStreamEntry(
        int StreamTypeCode,
        ushort? Pid,
        int? SubPathId,
        int? SubClipId);

    private sealed record ParsedStreamAttributes(
        byte CodingTypeCode,
        int? FormatCode,
        int? RateCode);
}
