using TheDiscDb.OpticalDiscParsers.Bdmv;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Input;

namespace TheDiscDb.OpticalDiscParsers.Tests;

/// <summary>
/// Tests for the BDMV parser against Blu-ray navigation fixtures.
/// </summary>
public class BdmvParserTests
{
    private readonly string fixturesPath = Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        "BDMV"
    );

    [Theory]
    [InlineData("BD-A", "0200", "E1 Entertainment", 11, "HDMV", "HDMV")]
    [InlineData("BD-B", "0300", "Provider Name", 90, "BD-J", "BD-J")]
    public async Task ParseIndexAsync_Samples_ReadsAppInfoAndTitleTable(
        string discName,
        string expectedVersion,
        string expectedUserData,
        int expectedTitleCount,
        string expectedFirstPlaybackType,
        string expectedTopMenuType)
    {
        var path = Path.Combine(fixturesPath, discName, "index.bdmv");
        Assert.True(File.Exists(path), $"Fixture not found: {path}");

        var parser = CreateParser(path);
        var result = await parser.ParseIndexAsync();

        Assert.NotNull(result.Value);
        var index = result.Value!;

        Assert.Equal("INDX", index.Identifier);
        Assert.Equal(expectedVersion, index.Version);
        Assert.Equal(34u, index.AppInfo.Length);
        Assert.Equal(expectedUserData, index.AppInfo.UserData);
        Assert.Equal(expectedTitleCount, index.Titles.Count);
        Assert.Equal(expectedFirstPlaybackType, index.FirstPlayback.ObjectType);
        Assert.Equal(expectedTopMenuType, index.TopMenu.ObjectType);
        Assert.Empty(index.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task ParseIndexAsync_BdjDisc_ReadsBdjApplicationNames()
    {
        var path = Path.Combine(fixturesPath, "BD-B", "index.bdmv");
        var parser = CreateParser(path);

        var result = await parser.ParseIndexAsync();

        Assert.NotNull(result.Value);
        var index = result.Value!;

        Assert.Equal("00002", index.FirstPlayback.Name);
        Assert.Equal("00000", index.TopMenu.Name);
        Assert.Contains(index.Titles, title => title.Object.ObjectType == "BD-J" && title.Object.Name == "00001");
    }

    [Theory]
    [InlineData("BD-A", "0200", 13, 22)]
    [InlineData("BD-B", "0300", 3, 2000)]
    public async Task ParseMovieObjectsAsync_Samples_ReadsObjectsAndCommands(
        string discName,
        string expectedVersion,
        int expectedObjectCount,
        int expectedFirstObjectCommands)
    {
        var path = Path.Combine(fixturesPath, discName, "MovieObject.bdmv");
        Assert.True(File.Exists(path), $"Fixture not found: {path}");

        var parser = CreateParser(path);
        var result = await parser.ParseMovieObjectsAsync();

        Assert.NotNull(result.Value);
        var movieObjects = result.Value!;

        Assert.Equal("MOBJ", movieObjects.Identifier);
        Assert.Equal(expectedVersion, movieObjects.Version);
        Assert.Equal(expectedObjectCount, movieObjects.Objects.Count);
        Assert.Equal(expectedFirstObjectCommands, movieObjects.Objects[0].NumberOfCommands);
        Assert.Equal(movieObjects.Objects[0].NumberOfCommands, movieObjects.Objects[0].Commands.Count);
        Assert.Empty(movieObjects.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task ParseMovieObjectsAsync_BdjDisc_ReadsCommandFields()
    {
        var path = Path.Combine(fixturesPath, "BD-B", "MovieObject.bdmv");
        var parser = CreateParser(path);

        var result = await parser.ParseMovieObjectsAsync();

        Assert.NotNull(result.Value);
        var firstCommand = result.Value!.Objects[0].Commands[0];

        Assert.Equal(0, firstCommand.Index);
        Assert.True(firstCommand.OperationCount >= 0);
        Assert.True(firstCommand.Destination > 0 || firstCommand.Source > 0);
    }

    private static BdmvParser CreateParser(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        return new BdmvParser(binaryReader);
    }
}
