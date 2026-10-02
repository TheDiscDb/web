using TheDiscDb.OpticalDiscManifest.Generation;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscParsers.Dvd;
using TheDiscDb.OpticalDiscParsers.Dvd.Models;
using TheDiscDb.OpticalDiscParsers.Input;

namespace TheDiscDb.OpticalDiscParsers.Tests;

public sealed class DvdManifestBuilderTests
{
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
