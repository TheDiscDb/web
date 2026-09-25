using TheDiscDb.OpticalDiscParsers.Bdmv;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Input;

namespace TheDiscDb.OpticalDiscParsers.Tests;

/// <summary>
/// Tests for the Blu-ray CLPI parser against real clip information fixtures.
/// </summary>
public class ClpiParserTests
{
    private readonly string fixturesPath = Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        "CLPI"
    );

    [Theory]
    [InlineData("BD-A", "00000.clpi", "0200")]
    [InlineData("UHD-A", "00589.clpi", "0300")]
    public async Task ParseAsync_Samples_ReadsHeaderAndSectionOffsets(string discName, string filename, string expectedVersion)
    {
        var path = Path.Combine(fixturesPath, discName, filename);
        Assert.True(File.Exists(path), $"Fixture not found: {path}");

        var parser = CreateParser(path);
        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var clpi = result.Value!;

        Assert.Equal("HDMV", clpi.Identifier);
        Assert.Equal(expectedVersion, clpi.Version);
        Assert.True(clpi.SequenceInfoStartAddress > 0);
        Assert.True(clpi.ProgramInfoStartAddress > clpi.SequenceInfoStartAddress);
        Assert.True(clpi.CpiStartAddress > clpi.ProgramInfoStartAddress);
        Assert.True(clpi.ClipMarkStartAddress > clpi.CpiStartAddress);
        Assert.Empty(clpi.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("BD-A", "00000.clpi")]
    [InlineData("UHD-A", "00589.clpi")]
    public async Task ParseAsync_FeatureClips_ReadsClipInfoSequenceAndPrograms(string discName, string filename)
    {
        var parser = CreateParser(Path.Combine(fixturesPath, discName, filename));

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var clpi = result.Value!;

        Assert.True(clpi.ClipInfo.Length > 0);
        Assert.True(clpi.ClipInfo.NumberOfSourcePackets > 0);
        Assert.NotNull(clpi.PresentationSummary);
        Assert.True(clpi.PresentationSummary.DurationTicks45k > 0);
        Assert.NotEmpty(clpi.AtcSequences);
        Assert.NotEmpty(clpi.AtcSequences[0].StcSequences);
        Assert.NotEmpty(clpi.Programs);
        Assert.NotEmpty(clpi.Programs[0].Streams);
        Assert.Contains(clpi.Programs.SelectMany(program => program.Streams), stream => stream.CodingType.Contains("Video"));
    }

    [Fact]
    public async Task ParseAsync_UhdFeature_ReadsHevcAndCpiEntries()
    {
        var parser = CreateParser(Path.Combine(fixturesPath, "UHD-A", "00589.clpi"));

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var clpi = result.Value!;
        var streams = clpi.Programs.SelectMany(program => program.Streams).ToList();
        var hevc = streams.First(stream => stream.CodingType == "HEVC Video");

        Assert.Equal("Video", hevc.Category);
        Assert.Equal(0x1011, hevc.Pid);
        Assert.Equal(21, hevc.AttributeLength);
        Assert.Equal("248130120000000000000000000000000000000000", hevc.RawAttributesHex);
        Assert.Equal(8, hevc.FormatCode);
        Assert.Equal(1, hevc.RateCode);
        Assert.Equal(3, hevc.AspectCode);
        Assert.False(hevc.OcFlag);
        Assert.False(hevc.CrFlag);
        Assert.Equal(1, hevc.DynamicRangeTypeCode);
        Assert.Equal(2, hevc.ColorSpaceCode);
        Assert.False(hevc.HdrPlusFlag);
        Assert.Equal(524280u, clpi.PresentationSummary!.StartTime);
        Assert.Equal(423153361u, clpi.PresentationSummary.EndTime);
        Assert.Equal(422629081u, clpi.PresentationSummary.DurationTicks45k);
        Assert.Equal(1, clpi.PresentationSummary.StcSequenceCount);
        Assert.Contains(streams, stream => stream.LanguageCode == "eng");
        Assert.NotEmpty(clpi.CpiEntries);
        Assert.All(clpi.CpiEntries, entry => Assert.True(entry.NumberOfFineEntries >= entry.NumberOfCoarseEntries));
    }

    [Fact]
    public async Task ParseAsync_MvcDependentClip_ReadsMvcStream()
    {
        var parser = CreateParser(Path.Combine(fixturesPath, "BD-3D", "00301.clpi"));

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var clpi = result.Value!;
        Assert.Empty(clpi.Programs.SelectMany(program => program.Streams));
        var mvc = Assert.Single(clpi.ExtensionStreams, stream => stream.CodingTypeCode == 0x20);

        Assert.Equal("Video", mvc.Category);
        Assert.Equal("MVC Video", mvc.CodingType);
        Assert.Equal(0x1012, mvc.Pid);
        Assert.Equal(6, mvc.FormatCode);
        Assert.Equal(1, mvc.RateCode);
        Assert.Empty(clpi.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task ParseAsync_TruncatedStreamAttributes_ReportsDiagnostic()
    {
        var path = Path.Combine(fixturesPath, "UHD-A", "00589.clpi");
        var bytes = File.ReadAllBytes(path);
        var firstStreamAttributeStart = GetFirstProgramStreamAttributeStart(bytes);
        Array.Resize(ref bytes, firstStreamAttributeStart + 2);
        var parser = CreateParser(bytes);

        var result = await parser.ParseAsync();

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CL011");
    }

    [Fact]
    public async Task ParseAsync_AllFixtures_ParseWithoutCriticalErrors()
    {
        var files = Directory.GetFiles(fixturesPath, "*.clpi", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var parser = CreateParser(file);
            var result = await parser.ParseAsync();

            Assert.NotNull(result.Value);
            Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        }
    }

    private static ClpiParser CreateParser(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return CreateParser(bytes);
    }

    private static ClpiParser CreateParser(byte[] bytes)
    {
        var reader = new MemoryOpticalDiscReader(bytes);
        var binaryReader = new OpticalDiscBinaryReader(reader);
        return new ClpiParser(binaryReader);
    }

    private static int GetFirstProgramStreamAttributeStart(byte[] bytes)
    {
        int programInfoStart = (int)ReadUInt32(bytes, 12);
        int offset = programInfoStart + 6;
        offset += 8;
        offset += 2;
        offset += 1;
        return offset;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
        => ((uint)bytes[offset] << 24)
            | ((uint)bytes[offset + 1] << 16)
            | ((uint)bytes[offset + 2] << 8)
            | bytes[offset + 3];
}
