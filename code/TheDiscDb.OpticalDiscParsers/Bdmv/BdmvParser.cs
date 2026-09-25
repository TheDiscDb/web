namespace TheDiscDb.OpticalDiscParsers.Bdmv;

using System.Buffers.Binary;
using System.Text;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Input;
using TheDiscDb.OpticalDiscParsers.Models;

/// <summary>
/// Parses Blu-ray Disc Movie (BDMV) navigation metadata files.
/// </summary>
public sealed class BdmvParser
{
    private readonly IOpticalDiscReader source;

    /// <summary>
    /// Creates a new BDMV parser.
    /// </summary>
    public BdmvParser(IOpticalDiscReader source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// Parses an index.bdmv file.
    /// </summary>
    public ValueTask<ParserResult<BdmvIndex>> ParseIndexAsync()
    {
        var context = new ParseContext(source);
        return new BdmvParseSession(context).ParseIndexAsync();
    }

    /// <summary>
    /// Parses a MovieObject.bdmv file.
    /// </summary>
    public ValueTask<ParserResult<BdmvMovieObjects>> ParseMovieObjectsAsync()
    {
        var context = new ParseContext(source);
        return new BdmvParseSession(context).ParseMovieObjectsAsync();
    }
}

internal sealed class BdmvParseSession
{
    private readonly OpticalDiscBinaryReader reader;
    private readonly DiagnosticBag diagnostics;

    public BdmvParseSession(ParseContext context)
    {
        reader = context.Reader;
        diagnostics = context.Diagnostics;
    }

    public async ValueTask<ParserResult<BdmvIndex>> ParseIndexAsync()
    {
        var identifier = await ReadStringAtAsync(0, 4);
        var version = await ReadStringAtAsync(4, 4);
        if (identifier != "INDX")
        {
            diagnostics.Error(0, "BD001", $"Expected index.bdmv identifier 'INDX', got '{identifier ?? "<null>"}'");
            return new ParserResult<BdmvIndex>(null, diagnostics.Snapshot());
        }

        if (version is null)
        {
            diagnostics.Error(4, "BD002", "Missing index.bdmv version");
            return new ParserResult<BdmvIndex>(null, diagnostics.Snapshot());
        }

        uint indexStartAddress = await ReadUInt32AtAsync(8);
        uint extensionDataStartAddress = await ReadUInt32AtAsync(12);
        var appInfo = await ParseAppInfoAsync();
        var (firstPlayback, topMenu, titles) = await ParseIndexTableAsync(indexStartAddress);

        var index = new BdmvIndex
        {
            Identifier = identifier,
            Version = version,
            IndexStartAddress = indexStartAddress,
            ExtensionDataStartAddress = extensionDataStartAddress,
            AppInfo = appInfo,
            FirstPlayback = firstPlayback,
            TopMenu = topMenu,
            Titles = titles
        };

        return new ParserResult<BdmvIndex>(index, diagnostics.Snapshot());
    }

    public async ValueTask<ParserResult<BdmvMovieObjects>> ParseMovieObjectsAsync()
    {
        var identifier = await ReadStringAtAsync(0, 4);
        var version = await ReadStringAtAsync(4, 4);
        if (identifier != "MOBJ")
        {
            diagnostics.Error(0, "BD010", $"Expected MovieObject.bdmv identifier 'MOBJ', got '{identifier ?? "<null>"}'");
            return new ParserResult<BdmvMovieObjects>(null, diagnostics.Snapshot());
        }

        if (version is null)
        {
            diagnostics.Error(4, "BD011", "Missing MovieObject.bdmv version");
            return new ParserResult<BdmvMovieObjects>(null, diagnostics.Snapshot());
        }

        uint extensionDataStartAddress = await ReadUInt32AtAsync(8);

        reader.Seek(40);
        uint dataLength = await reader.ReadUInt32BigEndianAsync();
        _ = await reader.ReadUInt32BigEndianAsync();
        ushort numberOfObjects = await reader.ReadUInt16BigEndianAsync();

        var objects = new List<BdmvMovieObject>(numberOfObjects);
        for (int i = 0; i < numberOfObjects; i++)
        {
            var movieObject = await ParseMovieObjectAsync(i);
            if (movieObject is null)
            {
                break;
            }

            objects.Add(movieObject);
        }

        var movieObjects = new BdmvMovieObjects
        {
            Identifier = identifier,
            Version = version,
            ExtensionDataStartAddress = extensionDataStartAddress,
            DataLength = dataLength,
            Objects = objects
        };

        return new ParserResult<BdmvMovieObjects>(movieObjects, diagnostics.Snapshot());
    }

    private async ValueTask<BdmvAppInfo> ParseAppInfoAsync()
    {
        uint length = await ReadUInt32AtAsync(40);
        var payload = await ReadBytesAtAsync(44, (int)length);
        if (payload.Length < 34)
        {
            diagnostics.Warning(40, "BD003", $"AppInfoBDMV block is truncated; expected 34 bytes, got {payload.Length}");
            return new BdmvAppInfo
            {
                Length = length,
                InitialOutputModePreference = false,
                ContentExists = false,
                VideoFormatCode = 0,
                FrameRateCode = 0,
                UserData = string.Empty
            };
        }

        byte flags = payload.Span[0];
        byte video = payload.Span[1];
        var userData = DecodeAscii(payload.Slice(2, 32));

        return new BdmvAppInfo
        {
            Length = length,
            InitialOutputModePreference = (flags & 0x40) != 0,
            ContentExists = (flags & 0x20) != 0,
            VideoFormatCode = (video >> 4) & 0x0F,
            FrameRateCode = video & 0x0F,
            UserData = userData
        };
    }

    private async ValueTask<(BdmvNavigationObject FirstPlayback, BdmvNavigationObject TopMenu, IReadOnlyList<BdmvTitle> Titles)> ParseIndexTableAsync(uint indexStartAddress)
    {
        if (indexStartAddress == 0)
        {
            diagnostics.Warning(8, "BD004", "index.bdmv index table start address is 0");
            var emptyObject = ParseNavigationObject(ReadOnlyMemory<byte>.Empty);
            return (emptyObject, emptyObject, Array.Empty<BdmvTitle>());
        }

        reader.Seek(indexStartAddress);
        uint indexLength = await reader.ReadUInt32BigEndianAsync();
        var firstPlaybackData = await reader.ReadBytesAsync(12);
        var topMenuData = await reader.ReadBytesAsync(12);
        if (firstPlaybackData.Length < 12 || topMenuData.Length < 12)
        {
            diagnostics.Warning(indexStartAddress, "BD005", "index.bdmv index table is truncated before first-play/top-menu entries");
            var emptyObject = ParseNavigationObject(ReadOnlyMemory<byte>.Empty);
            return (emptyObject, emptyObject, Array.Empty<BdmvTitle>());
        }

        var firstPlayback = ParseNavigationObject(firstPlaybackData);
        var topMenu = ParseNavigationObject(topMenuData);
        ushort numberOfTitles = await reader.ReadUInt16BigEndianAsync();
        uint expectedTitleBytes = (uint)numberOfTitles * 12;
        if (indexLength < 26 || indexLength - 26 < expectedTitleBytes)
        {
            diagnostics.Warning(indexStartAddress, "BD006", $"index.bdmv title table length {indexLength} is smaller than declared title count {numberOfTitles}");
        }

        var titles = new List<BdmvTitle>(numberOfTitles);
        for (int i = 0; i < numberOfTitles; i++)
        {
            var titleData = await reader.ReadBytesAsync(12);
            if (titleData.Length < 12)
            {
                diagnostics.Warning(indexStartAddress + 30 + (i * 12), "BD007", $"index.bdmv title entry {i} is truncated");
                break;
            }

            int accessTypeCode = (titleData.Span[0] >> 4) & 0x03;
            titles.Add(new BdmvTitle
            {
                Index = i,
                AccessTypeCode = accessTypeCode,
                AccessType = GetAccessTypeName(accessTypeCode),
                Object = ParseNavigationObject(titleData)
            });
        }

        return (firstPlayback, topMenu, titles);
    }

    private async ValueTask<BdmvMovieObject?> ParseMovieObjectAsync(int index)
    {
        var header = await reader.ReadBytesAsync(4);
        if (header.Length < 4)
        {
            diagnostics.Warning(reader.Position, "BD012", $"Movie object {index} header is truncated");
            return null;
        }

        byte flags = header.Span[0];
        int numberOfCommands = BinaryPrimitives.ReadUInt16BigEndian(header.Span[2..4]);
        var commands = new List<BdmvNavigationCommand>(numberOfCommands);
        for (int i = 0; i < numberOfCommands; i++)
        {
            var commandData = await reader.ReadBytesAsync(12);
            if (commandData.Length < 12)
            {
                diagnostics.Warning(reader.Position, "BD013", $"Movie object {index} command {i} is truncated");
                break;
            }

            commands.Add(ParseCommand(i, commandData.Span));
        }

        return new BdmvMovieObject
        {
            Index = index,
            ResumeIntentionFlag = (flags & 0x80) != 0,
            MenuCallMask = (flags & 0x40) != 0,
            TitleSearchMask = (flags & 0x20) != 0,
            NumberOfCommands = numberOfCommands,
            Commands = commands
        };
    }

    private static BdmvNavigationObject ParseNavigationObject(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 12)
        {
            return new BdmvNavigationObject
            {
                ObjectTypeCode = 0,
                ObjectType = "Unknown",
                PlaybackTypeCode = 0,
                PlaybackType = "Unknown"
            };
        }

        int objectTypeCode = (data.Span[0] >> 6) & 0x03;
        int playbackTypeCode = (data.Span[4] >> 6) & 0x03;

        if (objectTypeCode == 1)
        {
            return new BdmvNavigationObject
            {
                ObjectTypeCode = objectTypeCode,
                ObjectType = "HDMV",
                PlaybackTypeCode = playbackTypeCode,
                PlaybackType = GetPlaybackTypeName(playbackTypeCode),
                IdReference = BinaryPrimitives.ReadUInt16BigEndian(data.Span[6..8])
            };
        }

        if (objectTypeCode == 2)
        {
            return new BdmvNavigationObject
            {
                ObjectTypeCode = objectTypeCode,
                ObjectType = "BD-J",
                PlaybackTypeCode = playbackTypeCode,
                PlaybackType = GetPlaybackTypeName(playbackTypeCode),
                Name = DecodeAscii(data.Slice(6, 5))
            };
        }

        return new BdmvNavigationObject
        {
            ObjectTypeCode = objectTypeCode,
            ObjectType = "Unknown",
            PlaybackTypeCode = playbackTypeCode,
            PlaybackType = GetPlaybackTypeName(playbackTypeCode)
        };
    }

    private static BdmvNavigationCommand ParseCommand(int index, ReadOnlySpan<byte> data)
    {
        return new BdmvNavigationCommand
        {
            Index = index,
            OperationCount = (data[0] >> 5) & 0x07,
            Group = (data[0] >> 3) & 0x03,
            SubGroup = data[0] & 0x07,
            ImmediateOperand1 = (data[1] & 0x80) != 0,
            ImmediateOperand2 = (data[1] & 0x40) != 0,
            BranchOption = data[1] & 0x0F,
            CompareOption = data[2] & 0x0F,
            SetOption = data[3] & 0x1F,
            Destination = BinaryPrimitives.ReadUInt32BigEndian(data[4..8]),
            Source = BinaryPrimitives.ReadUInt32BigEndian(data[8..12])
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
            diagnostics.Warning(offset, "BD014", $"Expected {sizeof(uint)} bytes, got {bytes.Length}");
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
        while (length > 0 && (bytes.Span[length - 1] == 0 || bytes.Span[length - 1] == 0x20))
        {
            length--;
        }

        return Encoding.ASCII.GetString(bytes.Span[..length]);
    }

    private static string GetPlaybackTypeName(int playbackTypeCode)
    {
        return playbackTypeCode switch
        {
            0 => "Movie",
            1 => "Interactive",
            _ => "Unknown"
        };
    }

    private static string GetAccessTypeName(int accessTypeCode)
    {
        return accessTypeCode switch
        {
            0 => "Normal",
            1 => "Hidden",
            2 => "Pop-up",
            _ => "Unknown"
        };
    }
}
