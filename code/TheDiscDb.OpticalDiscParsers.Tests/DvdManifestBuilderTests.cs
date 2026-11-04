using TheDiscDb.OpticalDiscManifest.Generation;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Dvd;
using TheDiscDb.OpticalDiscParsers.Dvd.Models;
using TheDiscDb.OpticalDiscParsers.Input;

namespace TheDiscDb.OpticalDiscParsers.Tests;

public sealed class DvdManifestBuilderTests
{
    [Theory]
    [InlineData(0x02)]
    [InlineData(0x40)]
    public async Task BuildTitles_TitleTypeFlagsDoNotDisableSingleChainTiming(byte flags)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "fixtures", "DVD-A");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(root, "VIDEO_TS.IFO"), TestContext.Current.CancellationToken);
        int table = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0xC4, 4)) * 2048);
        bytes[table + 8] = flags;
        var vmgi = (await new DvdIfoParser(new MemoryOpticalDiscReader(bytes)).ParseVmgiAsync()).Value!;
        var fixture = await ReadFixtureAsync();
        var diagnostics = new List<ManifestDiagnostic>();
        var title = Assert.Single(DvdManifestBuilder.BuildTitles(
            vmgi with { Titles = [vmgi.Titles[0]] },
            new Dictionary<int, VtsiHeader> { [1] = fixture.Vtsi }, diagnostics));

        Assert.Equal(35, title.Chapters!.Count);
        Assert.True(title.DurationSeconds > 0);
        Assert.Equal(4_459_864_064L, title.SizeBytes);
        Assert.NotEmpty(title.Segments!);
        Assert.DoesNotContain(diagnostics, item => item.Code == "ODM_DVD_TIMING_PARTIAL");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildTitles_MultiPgcTitleIsFirstChainDeclaringWholeSeconds(bool allChainsZero)
    {
        var fixture = await ReadFixtureAsync();
        var map = fixture.Vtsi.TitlePartMaps.Single(item => item.TitleSetTitleNumber == fixture.Title.TitleSetTitleNumber);
        int realPgc = map.Parts[0].ProgramChainNumber;
        static ProgramChain ZeroSeconds(ProgramChain pgc, int number) => pgc with
        {
            Number = number,
            CellPlayback = pgc.CellPlayback
                .Select(cell => cell with { PlaybackTime = cell.PlaybackTime with { Hours = 0, Minutes = 0, Seconds = 0 } })
                .ToArray(),
        };
        var source = fixture.Vtsi.ProgramChains.Single(pgc => pgc.Number == realPgc);
        var chains = fixture.Vtsi.ProgramChains
            .Select(pgc => allChainsZero && pgc.Number == realPgc ? ZeroSeconds(pgc, pgc.Number) : pgc)
            .Append(ZeroSeconds(source, 99))
            .ToArray();
        var parts = map.Parts
            .Prepend(map.Parts[0] with { ProgramChainNumber = 99 })
            .Select((part, index) => part with { Number = index + 1 })
            .ToArray();
        var vtsi = fixture.Vtsi with { ProgramChains = chains, TitlePartMaps = [map with { Parts = parts }] };
        var vmgiTitle = fixture.Title with
        {
            NumberOfPartsOfTitle = parts.Length,
            PlaybackFlags = fixture.Title.PlaybackFlags with { IsMultiOrRandomPgcTitle = true },
        };
        var diagnostics = new List<ManifestDiagnostic>();

        var titles = DvdManifestBuilder.BuildTitles(
            fixture.Vmgi with { Titles = [vmgiTitle] }, new Dictionary<int, VtsiHeader> { [1] = vtsi }, diagnostics);

        if (allChainsZero)
        {
            Assert.Empty(titles);
            Assert.Contains(diagnostics, item => item.Code == "ODM_DVD_TITLE_SKIPPED");
            return;
        }

        var title = Assert.Single(titles);
        Assert.Equal(35, title.Chapters!.Count);
        Assert.Equal(4_459_864_064L, title.SizeBytes);
        Assert.DoesNotContain(diagnostics, item => item.Code == "ODM_DVD_TIMING_PARTIAL");
    }

    [Fact]
    public async Task BuildTitles_PartiallyUnresolvedProgramsRetainIdentityButOmitTimingAndSize()
    {
        var fixture = await ReadFixtureAsync();
        var map = fixture.Vtsi.TitlePartMaps.Single(item => item.TitleSetTitleNumber == fixture.Title.TitleSetTitleNumber);
        var modifiedMap = map with
        {
            Parts =
            [
                map.Parts[0],
                map.Parts[1] with { ProgramChainNumber = int.MaxValue },
            ],
        };
        var vmgi = fixture.Vmgi with { Titles = [fixture.Title with { NumberOfPartsOfTitle = 2 }] };
        var vtsi = fixture.Vtsi with { TitlePartMaps = [modifiedMap] };
        var diagnostics = new List<ManifestDiagnostic>();

        var title = Assert.Single(DvdManifestBuilder.BuildTitles(
            vmgi, new Dictionary<int, VtsiHeader> { [1] = vtsi }, diagnostics));

        Assert.Equal(fixture.Title.Number, title.Source.Title);
        Assert.Equal(1, title.Source.TitleSet);
        Assert.Null(title.DurationSeconds);
        Assert.Null(title.SizeBytes);
        Assert.Null(title.Chapters);
        Assert.Null(title.Segments);
        Assert.NotEmpty(title.Streams!);
        Assert.Contains(diagnostics, item => item.Code == "ODM_DVD_PARTIAL" && item.Message.Contains("unavailable PGC"));
        Assert.Contains(diagnostics, item => item.Code == "ODM_DVD_TIMING_PARTIAL" && item.Severity == "warning");
        Assert.DoesNotContain(diagnostics, item => item.Code == "ODM_DVD_TITLE_SKIPPED" || item.Message.StartsWith("Only "));
    }

    [Fact]
    public async Task BuildTitles_InvalidCellMapsAreIntentionalSkipsNotUnresolvedJoins()
    {
        var fixture = await ReadFixtureAsync();
        var vtsi = fixture.Vtsi with
        {
            ProgramChains = fixture.Vtsi.ProgramChains.Select(pgc => pgc with { ProgramMap = [] }).ToArray(),
        };
        var diagnostics = new List<ManifestDiagnostic>();
        var vmgi = fixture.Vmgi with { Titles = [fixture.Title] };

        var titles = DvdManifestBuilder.BuildTitles(
            vmgi, new Dictionary<int, VtsiHeader> { [1] = vtsi }, diagnostics);

        Assert.Empty(titles);
        Assert.Equal("ODM_DVD_TITLE_SKIPPED", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public async Task BuildTitles_MissingPartMapReportsUnresolvedJoinRatherThanIntentionalSkip()
    {
        var fixture = await ReadFixtureAsync();
        var diagnostics = new List<ManifestDiagnostic>();
        var vmgi = fixture.Vmgi with { Titles = [fixture.Title] };
        var vtsi = fixture.Vtsi with { TitlePartMaps = [] };

        var titles = DvdManifestBuilder.BuildTitles(
            vmgi, new Dictionary<int, VtsiHeader> { [1] = vtsi }, diagnostics);

        Assert.Empty(titles);
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, item => Assert.Equal("ODM_DVD_PARTIAL", item.Code));
        Assert.Contains(diagnostics, item => item.Message.StartsWith("Only 0 of 1"));
    }

    [Fact]
    public async Task BuildTitles_StreamsFollowPgcAvailabilityAndPhysicalMapping()
    {
        var fixture = await ReadFixtureAsync();
        Assert.True(fixture.Vtsi.AudioStreams.Count >= 2 && fixture.Vtsi.SubtitleStreams.Count >= 2);
        var pgcNumbers = fixture.Vtsi.TitlePartMaps
            .Single(item => item.TitleSetTitleNumber == fixture.Title.TitleSetTitleNumber)
            .Parts.Select(part => part.ProgramChainNumber).ToHashSet();
        var vtsi = fixture.Vtsi with
        {
            ProgramChains = fixture.Vtsi.ProgramChains.Select(pgc => pgcNumbers.Contains(pgc.Number)
                ? pgc with
                {
                    AudioControls = Enumerable.Range(0, 8).Select(i => new DvdAudioStreamControl
                    {
                        StreamIndex = i, RawValue = 0, IsAvailable = i == 1, PlayerStreamNumber = i == 1 ? 1 : null,
                    }).ToArray(),
                    SubpictureControls = Enumerable.Range(0, 32).Select(i => new DvdSubpictureStreamControl
                    {
                        StreamIndex = i, RawValue = 0x80000000, IsAvailable = true,
                    }).ToArray(),
                }
                : pgc).ToArray(),
        };

        var title = Assert.Single(DvdManifestBuilder.BuildTitles(
            fixture.Vmgi with { Titles = [fixture.Title] },
            new Dictionary<int, VtsiHeader> { [1] = vtsi }, new List<ManifestDiagnostic>()));

        var audio = Assert.Single(title.Streams!, item => item.Type == "audio");
        Assert.Equal(fixture.Vtsi.AudioStreams.Single(item => item.Index == 1).Codec, audio.Codec);
        Assert.Single(title.Streams!, item => item.Type == "subtitle");
    }

    [Fact]
    public async Task BuildTitles_MultiAngleTitleIsListedOncePerAngle()
    {
        var fixture = await ReadFixtureAsync();
        var map = fixture.Vtsi.TitlePartMaps.Single(item => item.TitleSetTitleNumber == fixture.Title.TitleSetTitleNumber);
        var pgc = fixture.Vtsi.ProgramChains.Single(item => item.Number == map.Parts[0].ProgramChainNumber);
        var single = Assert.Single(DvdManifestBuilder.BuildTitles(
            fixture.Vmgi with { Titles = [fixture.Title] },
            new Dictionary<int, VtsiHeader> { [1] = fixture.Vtsi },
            new List<ManifestDiagnostic>()));

        // Merge programs 1-2 and make their two cells a two-angle block, as on Kiki's Delivery
        // Service, whose angle blocks sit inside a program.
        int firstCell = pgc.ProgramMap[0].FirstCellNumber;
        Assert.Equal(firstCell + 1, pgc.ProgramMap[1].FirstCellNumber);
        var angleBlock = pgc with
        {
            NumberOfPrograms = pgc.NumberOfPrograms - 1,
            ProgramMap = pgc.ProgramMap.Where(entry => entry.ProgramNumber != 2)
                .Select((entry, index) => entry with { ProgramNumber = index + 1 }).ToArray(),
            CellPlayback = pgc.CellPlayback.Select(cell => cell.Number == firstCell || cell.Number == firstCell + 1
                ? cell with
                {
                    BlockType = DvdCellBlockType.AngleBlock,
                    BlockMode = cell.Number == firstCell ? DvdCellBlockMode.FirstCell : DvdCellBlockMode.LastCell,
                }
                : cell).ToArray(),
        };
        var parts = map.Parts.Where(part => !(part.ProgramChainNumber == pgc.Number && part.ProgramNumber == 2))
            .Select((part, index) => part with
            {
                Number = index + 1,
                ProgramNumber = part.ProgramChainNumber == pgc.Number && part.ProgramNumber > 2 ? part.ProgramNumber - 1 : part.ProgramNumber,
            })
            .ToArray();
        var vtsi = fixture.Vtsi with
        {
            ProgramChains = fixture.Vtsi.ProgramChains.Select(item => item.Number == pgc.Number ? angleBlock : item).ToArray(),
            TitlePartMaps = [map with { Parts = parts }],
        };
        var diagnostics = new List<ManifestDiagnostic>();

        var titles = DvdManifestBuilder.BuildTitles(
            fixture.Vmgi with { Titles = [fixture.Title with { NumberOfAngles = 2, NumberOfPartsOfTitle = parts.Length }] },
            new Dictionary<int, VtsiHeader> { [1] = vtsi },
            diagnostics);

        // Each angle plays the shared cells and its own block cell; its size counts an even share
        // of the block's sector span, since interleaved angle cells overlap.
        var first = pgc.CellPlayback[firstCell - 1];
        var second = pgc.CellPlayback[firstCell];
        static long Bytes(CellPlaybackInfo cell) => ((long)cell.LastSector - cell.FirstSector + 1) * 2048;
        long span = ((long)Math.Max(first.LastSector, second.LastSector) - Math.Min(first.FirstSector, second.FirstSector) + 1) * 2048;
        long expectedSize = single.SizeBytes!.Value - Bytes(first) - Bytes(second) + span / 2;
        Assert.Equal(2, titles.Count);
        for (int angle = 1; angle <= 2; angle++)
        {
            var title = titles[angle - 1];
            Assert.Equal(fixture.Title.Number, title.Source.Title);
            Assert.Equal(single.Chapters!.Count - 1, title.Chapters!.Count);
            Assert.Equal(expectedSize, title.SizeBytes);
            var blockSegment = Assert.Single(title.Segments!, segment => segment.Angle is not null);
            Assert.Equal(angle, blockSegment.Angle);
            Assert.Equal(firstCell + angle - 1, blockSegment.Cell);
            Assert.Equal(single.Segments!.Count - 1, title.Segments!.Count);
        }

        Assert.DoesNotContain(diagnostics, item => item.Code is "ODM_DVD_TIMING_PARTIAL" or "ODM_DVD_PARTIAL");
    }

    private static async Task<DvdFixture> ReadFixtureAsync()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "fixtures", "DVD-A");
        var vmgi = await new DvdIfoParser(new MemoryOpticalDiscReader(
            await File.ReadAllBytesAsync(Path.Combine(root, "VIDEO_TS.IFO"), TestContext.Current.CancellationToken)))
            .ParseVmgiAsync();
        var vtsi = await new DvdIfoParser(new MemoryOpticalDiscReader(
            await File.ReadAllBytesAsync(Path.Combine(root, "VTS_01_0.IFO"), TestContext.Current.CancellationToken)))
            .ParseVtsiAsync(1);
        Assert.NotNull(vmgi.Value);
        Assert.NotNull(vtsi.Value);
        return new DvdFixture(vmgi.Value, vtsi.Value, vmgi.Value.Titles[0]);
    }

    private sealed record DvdFixture(VmgiHeader Vmgi, VtsiHeader Vtsi, VmgiTitle Title);
}
