namespace TheDiscDb.OpticalDiscParsers.Bdmv;

using System.Buffers.Binary;
using System.Text;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Input;
using TheDiscDb.OpticalDiscParsers.Models;

/// <summary>
/// Parses Blu-ray clip information (.clpi) files.
/// </summary>
public sealed class ClpiParser
{
    private readonly IOpticalDiscReader source;

    /// <summary>
    /// Creates a new CLPI parser.
    /// </summary>
    public ClpiParser(IOpticalDiscReader source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// Parses a .clpi file.
    /// </summary>
    public ValueTask<ParserResult<ClpiFile>> ParseAsync()
    {
        var context = new ParseContext(source);
        return new ClpiParseSession(context).ParseAsync();
    }
}

internal sealed class ClpiParseSession
{
    private readonly OpticalDiscBinaryReader reader;
    private readonly DiagnosticBag diagnostics;

    public ClpiParseSession(ParseContext context)
    {
        reader = context.Reader;
        diagnostics = context.Diagnostics;
    }

    public async ValueTask<ParserResult<ClpiFile>> ParseAsync()
    {
        var identifier = await ReadStringAtAsync(0, 4);
        var version = await ReadStringAtAsync(4, 4);
        if (identifier != "HDMV")
        {
            diagnostics.Error(0, "CL001", $"Expected CLPI identifier 'HDMV', got '{identifier ?? "<null>"}'");
            return new ParserResult<ClpiFile>(null, diagnostics.Snapshot());
        }

        if (version is null)
        {
            diagnostics.Error(4, "CL002", "Missing CLPI version");
            return new ParserResult<ClpiFile>(null, diagnostics.Snapshot());
        }

        uint sequenceInfoStartAddress = await ReadUInt32AtAsync(8);
        uint programInfoStartAddress = await ReadUInt32AtAsync(12);
        uint cpiStartAddress = await ReadUInt32AtAsync(16);
        uint clipMarkStartAddress = await ReadUInt32AtAsync(20);
        uint extensionDataStartAddress = await ReadUInt32AtAsync(24);

        var clipInfo = await ParseClipInfoAsync();
        var atcSequences = await ParseSequenceInfoAsync(sequenceInfoStartAddress);
        var programs = await ParseProgramInfoAsync(programInfoStartAddress);
        var cpiEntries = await ParseCpiAsync(cpiStartAddress);
        var extensionStreams = await ParseExtensionStreamsAsync(extensionDataStartAddress);
        var presentationSummary = CreatePresentationSummary(atcSequences);
        var parserDiagnostics = diagnostics.Snapshot();

        var file = new ClpiFile
        {
            Identifier = identifier,
            Version = version,
            SequenceInfoStartAddress = sequenceInfoStartAddress,
            ProgramInfoStartAddress = programInfoStartAddress,
            CpiStartAddress = cpiStartAddress,
            ClipMarkStartAddress = clipMarkStartAddress,
            ExtensionDataStartAddress = extensionDataStartAddress,
            ClipInfo = clipInfo,
            AtcSequences = atcSequences,
            Programs = programs,
            ExtensionStreams = extensionStreams,
            CpiEntries = cpiEntries,
            PresentationSummary = presentationSummary
        };

        return new ParserResult<ClpiFile>(file, parserDiagnostics);
    }

    private async ValueTask<ClpiClipInfo> ParseClipInfoAsync()
    {
        reader.Seek(40);
        uint length = await reader.ReadUInt32BigEndianAsync();
        _ = await reader.ReadUInt16BigEndianAsync();
        byte clipStreamType = await reader.ReadByteAsync();
        byte applicationType = await reader.ReadByteAsync();
        uint flagsAndRateHigh = await reader.ReadUInt32BigEndianAsync();
        uint recordingRate = await reader.ReadUInt32BigEndianAsync();
        uint sourcePackets = await reader.ReadUInt32BigEndianAsync();
        bool isAtcDelta = (flagsAndRateHigh & 1) != 0;

        reader.Seek(58 + 128);
        ushort tsTypeInfoLength = await reader.ReadUInt16BigEndianAsync();
        byte? tsTypeValidity = null;
        string? tsTypeFormatIdentifier = null;
        if (tsTypeInfoLength >= 5)
        {
            tsTypeValidity = await reader.ReadByteAsync();
            tsTypeFormatIdentifier = await ReadStringAtCurrentAsync(4);
        }

        return new ClpiClipInfo
        {
            Length = length,
            ClipStreamType = clipStreamType,
            ApplicationType = applicationType,
            IsAtcDelta = isAtcDelta,
            TransportStreamRecordingRate = recordingRate,
            NumberOfSourcePackets = sourcePackets,
            TsTypeValidity = tsTypeValidity,
            TsTypeFormatIdentifier = tsTypeFormatIdentifier
        };
    }

    private async ValueTask<IReadOnlyList<ClpiAtcSequence>> ParseSequenceInfoAsync(uint sequenceInfoStartAddress)
    {
        if (sequenceInfoStartAddress == 0)
        {
            diagnostics.Warning(8, "CL003", "CLPI SequenceInfo start address is 0");
            return Array.Empty<ClpiAtcSequence>();
        }

        reader.Seek(sequenceInfoStartAddress);
        uint length = await reader.ReadUInt32BigEndianAsync();
        _ = await reader.ReadByteAsync();
        byte numberOfAtcSequences = await reader.ReadByteAsync();
        var atcSequences = new List<ClpiAtcSequence>(numberOfAtcSequences);

        for (int i = 0; i < numberOfAtcSequences; i++)
        {
            uint spnAtcStart = await reader.ReadUInt32BigEndianAsync();
            byte numberOfStcSequences = await reader.ReadByteAsync();
            byte offsetStcId = await reader.ReadByteAsync();
            var stcSequences = new List<ClpiStcSequence>(numberOfStcSequences);

            for (int j = 0; j < numberOfStcSequences; j++)
            {
                stcSequences.Add(new ClpiStcSequence
                {
                    Index = j,
                    PcrPid = await reader.ReadUInt16BigEndianAsync(),
                    SourcePacketNumberStcStart = await reader.ReadUInt32BigEndianAsync(),
                    PresentationStartTime = await reader.ReadUInt32BigEndianAsync(),
                    PresentationEndTime = await reader.ReadUInt32BigEndianAsync()
                });
            }

            atcSequences.Add(new ClpiAtcSequence
            {
                Index = i,
                SourcePacketNumberAtcStart = spnAtcStart,
                OffsetStcId = offsetStcId,
                StcSequences = stcSequences
            });
        }

        if (length == 0 && atcSequences.Count > 0)
        {
            diagnostics.Warning(sequenceInfoStartAddress, "CL004", "SequenceInfo length is 0 but entries were parsed");
        }

        return atcSequences;
    }

    private async ValueTask<IReadOnlyList<ClpiProgram>> ParseProgramInfoAsync(uint programInfoStartAddress)
    {
        if (programInfoStartAddress == 0)
        {
            diagnostics.Warning(12, "CL005", "CLPI ProgramInfo start address is 0");
            return Array.Empty<ClpiProgram>();
        }

        reader.Seek(programInfoStartAddress);
        uint length = await reader.ReadUInt32BigEndianAsync();
        _ = await reader.ReadByteAsync();
        byte numberOfPrograms = await reader.ReadByteAsync();
        var programs = new List<ClpiProgram>(numberOfPrograms);

        for (int i = 0; i < numberOfPrograms; i++)
        {
            uint spnProgramSequenceStart = await reader.ReadUInt32BigEndianAsync();
            ushort programMapPid = await reader.ReadUInt16BigEndianAsync();
            byte numberOfStreams = await reader.ReadByteAsync();
            byte numberOfGroups = await reader.ReadByteAsync();
            var streams = new List<ClpiProgramStream>(numberOfStreams);

            for (int j = 0; j < numberOfStreams; j++)
            {
                ushort pid = await reader.ReadUInt16BigEndianAsync();
                streams.Add(await ParseProgramStreamAsync(pid));
            }

            programs.Add(new ClpiProgram
            {
                Index = i,
                SourcePacketNumberProgramSequenceStart = spnProgramSequenceStart,
                ProgramMapPid = programMapPid,
                NumberOfGroups = numberOfGroups,
                Streams = streams
            });
        }

        if (length == 0 && programs.Count > 0)
        {
            diagnostics.Warning(programInfoStartAddress, "CL006", "ProgramInfo length is 0 but entries were parsed");
        }

        return programs;
    }

    private async ValueTask<ClpiProgramStream> ParseProgramStreamAsync(ushort pid)
    {
        byte length = await reader.ReadByteAsync();
        var data = await reader.ReadBytesAsync(length);
        if (data.Length < length)
        {
            diagnostics.Warning(reader.Position, "CL011", $"Stream attributes for PID 0x{pid:X4} are truncated: expected {length} bytes, got {data.Length}");
        }

        if (data.IsEmpty)
        {
            diagnostics.Warning(reader.Position, "CL007", $"Stream attributes for PID 0x{pid:X4} are empty");
            return CreateUnknownStream(pid, length, data);
        }

        return CreateProgramStream(pid, length, data);
    }

    private async ValueTask<IReadOnlyList<ClpiProgramStream>> ParseExtensionStreamsAsync(uint extensionDataStartAddress)
    {
        if (extensionDataStartAddress == 0)
        {
            return Array.Empty<ClpiProgramStream>();
        }

        var header = await ReadBytesAtAsync(extensionDataStartAddress, 12);
        if (header.Length < 12)
        {
            diagnostics.Warning(extensionDataStartAddress, "CL012", "CLPI ExtensionData header is truncated");
            return Array.Empty<ClpiProgramStream>();
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header.Span[0..4]);
        if (length == 0 || length > int.MaxValue - 4)
        {
            return Array.Empty<ClpiProgramStream>();
        }

        ReadOnlyMemory<byte> block = await ReadBytesAtAsync(extensionDataStartAddress, checked((int)length + 4));
        if (block.Length < length + 4)
        {
            diagnostics.Warning(extensionDataStartAddress, "CL013", $"CLPI ExtensionData is truncated: expected {length + 4} bytes, got {block.Length}");
        }

        if (block.Length < 12)
        {
            return Array.Empty<ClpiProgramStream>();
        }

        int entryCount = block.Span[11];
        int descriptorEnd = 12 + (entryCount * 12);
        if (descriptorEnd > block.Length || descriptorEnd > length + 4)
        {
            diagnostics.Warning(extensionDataStartAddress + 12, "CL014", $"CLPI ExtensionData descriptor table for {entryCount} entries is truncated");
            entryCount = Math.Max(0, (Math.Min(block.Length, checked((int)length + 4)) - 12) / 12);
        }

        var streams = new List<ClpiProgramStream>();
        for (int i = 0; i < entryCount; i++)
        {
            int descriptorOffset = 12 + (i * 12);
            ReadOnlySpan<byte> descriptor = block.Span.Slice(descriptorOffset, 12);
            ushort typeIdentifier = BinaryPrimitives.ReadUInt16BigEndian(descriptor[0..2]);
            ushort versionIdentifier = BinaryPrimitives.ReadUInt16BigEndian(descriptor[2..4]);
            uint relativeStartAddress = BinaryPrimitives.ReadUInt32BigEndian(descriptor[4..8]);
            uint entryLength = BinaryPrimitives.ReadUInt32BigEndian(descriptor[8..12]);
            ulong entryEnd = (ulong)relativeStartAddress + entryLength;
            if (relativeStartAddress > block.Length
                || entryLength > block.Length - relativeStartAddress
                || entryEnd > length + 4)
            {
                diagnostics.Warning(extensionDataStartAddress + descriptorOffset, "CL015", $"CLPI ExtensionData entry {i} extends past the declared extension block");
                continue;
            }

            if (typeIdentifier == 2 && versionIdentifier == 5)
            {
                streams.AddRange(ParseStereoscopicExtensionStreams(
                    block.Slice((int)relativeStartAddress, (int)entryLength),
                    extensionDataStartAddress + relativeStartAddress));
            }
        }

        return streams;
    }

    private IReadOnlyList<ClpiProgramStream> ParseStereoscopicExtensionStreams(ReadOnlyMemory<byte> data, long absoluteOffset)
    {
        if (data.Length < 14)
        {
            diagnostics.Warning(absoluteOffset, "CL016", "CLPI stereoscopic stream extension is truncated");
            return Array.Empty<ClpiProgramStream>();
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(data.Span[0..4]);
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.Span[4..6]);
        int end = Math.Min(data.Length, checked((int)length + 4));
        int offset = 14;
        var streams = new List<ClpiProgramStream>(count);
        for (int i = 0; i < count; i++)
        {
            if (offset + 3 > end)
            {
                diagnostics.Warning(absoluteOffset + offset, "CL017", $"CLPI stereoscopic stream {i} is truncated");
                break;
            }

            ushort pid = BinaryPrimitives.ReadUInt16BigEndian(data.Span.Slice(offset, 2));
            byte attributeLength = data.Span[offset + 2];
            int attributeStart = offset + 3;
            if (attributeLength > end - attributeStart)
            {
                diagnostics.Warning(absoluteOffset + offset, "CL018", $"CLPI stereoscopic stream {i} attributes extend past the extension entry");
                break;
            }

            streams.Add(CreateProgramStream(pid, attributeLength, data.Slice(attributeStart, attributeLength)));
            offset = attributeStart + attributeLength;
        }

        return streams;
    }

    private static ClpiProgramStream CreateProgramStream(ushort pid, byte length, ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            return CreateUnknownStream(pid, length, data);
        }

        byte codingType = data.Span[0];
        int? formatCode = null;
        int? rateCode = null;
        int? aspectCode = null;
        bool? ocFlag = null;
        bool? crFlag = null;
        int? dynamicRangeTypeCode = null;
        int? colorSpaceCode = null;
        bool? hdrPlusFlag = null;
        string? languageCode = null;
        int? characterCode = null;
        string? isrc = DecodeOptionalAscii(data.Length >= 17 ? data.Slice(5, 12) : ReadOnlyMemory<byte>.Empty);

        if (IsVideoCodingType(codingType) && data.Length >= 3)
        {
            formatCode = (data.Span[1] >> 4) & 0x0F;
            rateCode = data.Span[1] & 0x0F;
            aspectCode = (data.Span[2] >> 4) & 0x0F;
            ocFlag = (data.Span[2] & 0x02) != 0;
            if (codingType == 0x24 && data.Length >= 5)
            {
                crFlag = (data.Span[2] & 0x01) != 0;
                dynamicRangeTypeCode = (data.Span[3] >> 4) & 0x0F;
                colorSpaceCode = data.Span[3] & 0x0F;
                hdrPlusFlag = (data.Span[4] & 0x80) != 0;
            }
        }
        else if (IsAudioCodingType(codingType) && data.Length >= 5)
        {
            formatCode = (data.Span[1] >> 4) & 0x0F;
            rateCode = data.Span[1] & 0x0F;
            languageCode = DecodeAscii(data.Slice(2, 3));
        }
        else if (IsLanguageOnlyCodingType(codingType) && data.Length >= 4)
        {
            languageCode = DecodeAscii(data.Slice(1, 3));
        }
        else if (codingType == 0x92 && data.Length >= 5)
        {
            characterCode = data.Span[1];
            languageCode = DecodeAscii(data.Slice(2, 3));
        }

        return new ClpiProgramStream
        {
            Category = GetStreamCategory(codingType),
            Pid = pid,
            AttributeLength = length,
            RawAttributesHex = Convert.ToHexString(data.Span),
            CodingTypeCode = codingType,
            CodingType = GetCodingTypeName(codingType),
            FormatCode = formatCode,
            RateCode = rateCode,
            AspectCode = aspectCode,
            OcFlag = ocFlag,
            CrFlag = crFlag,
            DynamicRangeTypeCode = dynamicRangeTypeCode,
            ColorSpaceCode = colorSpaceCode,
            HdrPlusFlag = hdrPlusFlag,
            LanguageCode = languageCode,
            CharacterCode = characterCode,
            InternationalStandardRecordingCode = isrc
        };
    }

    private async ValueTask<IReadOnlyList<ClpiCpiEntry>> ParseCpiAsync(uint cpiStartAddress)
    {
        if (cpiStartAddress == 0)
        {
            return Array.Empty<ClpiCpiEntry>();
        }

        reader.Seek(cpiStartAddress);
        uint length = await reader.ReadUInt32BigEndianAsync();
        if (length == 0)
        {
            return Array.Empty<ClpiCpiEntry>();
        }

        var header = await reader.ReadBytesAsync(2);
        if (header.Length < 2)
        {
            diagnostics.Warning(cpiStartAddress, "CL008", "CPI header is truncated");
            return Array.Empty<ClpiCpiEntry>();
        }

        int type = header.Span[1] & 0x0F;
        long epMapPosition = reader.Position;
        _ = await reader.ReadByteAsync();
        byte numberOfStreamPids = await reader.ReadByteAsync();
        var entries = new List<ClpiCpiEntry>(numberOfStreamPids);

        for (int i = 0; i < numberOfStreamPids; i++)
        {
            var entryData = await reader.ReadBytesAsync(12);
            if (entryData.Length < 12)
            {
                diagnostics.Warning(reader.Position, "CL009", $"CPI entry {i} is truncated");
                break;
            }

            ushort pid = BinaryPrimitives.ReadUInt16BigEndian(entryData.Span[0..2]);
            ulong packed = BinaryPrimitives.ReadUInt64BigEndian(entryData.Span[2..10]);
            int epStreamType = (int)((packed >> 50) & 0x0F);
            int numEpCoarse = (int)((packed >> 34) & 0xFFFF);
            int numEpFine = (int)((packed >> 16) & 0x3FFFF);
            uint relativeStart = BinaryPrimitives.ReadUInt32BigEndian(entryData.Span[8..12]);

            entries.Add(new ClpiCpiEntry
            {
                Pid = pid,
                EntryPointStreamType = epStreamType == 0 && type != 0 ? type : epStreamType,
                NumberOfCoarseEntries = numEpCoarse,
                NumberOfFineEntries = numEpFine,
                EntryPointMapStreamStartAddress = (uint)(epMapPosition + relativeStart)
            });
        }

        return entries;
    }

    private static ClpiPresentationSummary? CreatePresentationSummary(IReadOnlyList<ClpiAtcSequence> atcSequences)
    {
        var sequences = atcSequences
            .SelectMany(sequence => sequence.StcSequences)
            .Where(sequence => sequence.PresentationEndTime >= sequence.PresentationStartTime)
            .ToArray();
        if (sequences.Length == 0)
        {
            return null;
        }

        uint start = sequences.Min(sequence => sequence.PresentationStartTime);
        uint end = sequences.Max(sequence => sequence.PresentationEndTime);
        return new ClpiPresentationSummary
        {
            StartTime = start,
            EndTime = end,
            DurationTicks45k = end - start,
            StcSequenceCount = sequences.Length
        };
    }

    private static ClpiProgramStream CreateUnknownStream(
        ushort pid,
        int attributeLength,
        ReadOnlyMemory<byte> rawAttributes)
    {
        return new ClpiProgramStream
        {
            Category = "Unknown",
            Pid = pid,
            AttributeLength = attributeLength,
            RawAttributesHex = Convert.ToHexString(rawAttributes.Span),
            CodingTypeCode = 0,
            CodingType = "Unknown"
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

    private async ValueTask<string> ReadStringAtCurrentAsync(int length)
    {
        var bytes = await reader.ReadBytesAsync(length);
        return DecodeAscii(bytes);
    }

    private async ValueTask<uint> ReadUInt32AtAsync(long offset)
    {
        var bytes = await ReadBytesAtAsync(offset, sizeof(uint));
        if (bytes.Length < sizeof(uint))
        {
            diagnostics.Warning(offset, "CL010", $"Expected {sizeof(uint)} bytes, got {bytes.Length}");
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

    private static string? DecodeOptionalAscii(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return null;
        }

        int length = bytes.Length;
        while (length > 0 && bytes.Span[length - 1] == 0)
        {
            length--;
        }

        return length == 0 ? null : Encoding.ASCII.GetString(bytes.Span[..length]);
    }

    private static bool IsVideoCodingType(byte codingType)
    {
        return codingType is 0x01 or 0x02 or 0xEA or 0x1B or 0x20 or 0x24;
    }

    private static bool IsAudioCodingType(byte codingType)
    {
        return codingType is 0x03 or 0x04 or 0x80 or 0x81 or 0x82 or 0x83 or 0x84 or 0x85 or 0x86 or 0xA1 or 0xA2;
    }

    private static bool IsLanguageOnlyCodingType(byte codingType)
    {
        return codingType is 0x90 or 0x91 or 0xA0;
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
            0xA0 => "Secondary Audio",
            0xA1 => "AC-3 Plus Secondary Audio",
            0xA2 => "DTS-HD Secondary Audio",
            0xEA => "VC-1 Video",
            _ => "Unknown"
        };
    }

    private static string GetStreamCategory(byte codingType)
    {
        if (codingType is 0xA0 or 0xA1 or 0xA2)
        {
            return "SecondaryAudio";
        }

        if (IsVideoCodingType(codingType))
        {
            return "Video";
        }

        if (IsAudioCodingType(codingType))
        {
            return "Audio";
        }

        return codingType switch
        {
            0x90 => "PresentationGraphics",
            0x91 => "InteractiveGraphics",
            0x92 => "TextSubtitle",
            0xA0 => "SecondaryAudio",
            _ => "Unknown"
        };
    }
}
