using TheDiscDb.OpticalDiscParsers.Bdmv;
using TheDiscDb.OpticalDiscParsers.Bdmv.Models;
using TheDiscDb.OpticalDiscParsers.Diagnostics;
using TheDiscDb.OpticalDiscParsers.Input;

namespace TheDiscDb.OpticalDiscParsers.Tests;

/// <summary>
/// Tests for the Blu-ray MPLS parser against real playlist fixtures.
/// </summary>
public class MplsParserTests
{
    private readonly string fixturesPath = Path.Combine(
        AppContext.BaseDirectory,
        "fixtures",
        "MPLS"
    );

    [Theory]
    [InlineData("BD-A", "00000.mpls", "0200")]
    [InlineData("UHD-A", "00149.mpls", "0300")]
    public async Task ParseAsync_Samples_ReadsHeaderAndPlaylistSections(string discName, string filename, string expectedVersion)
    {
        var path = Path.Combine(fixturesPath, discName, filename);
        Assert.True(File.Exists(path), $"Fixture not found: {path}");

        var parser = CreateParser(path);
        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var playlist = result.Value!;

        Assert.Equal("MPLS", playlist.Identifier);
        Assert.Equal(expectedVersion, playlist.Version);
        Assert.True(playlist.PlaylistStartAddress > 0);
        Assert.True(playlist.PlaylistMarkStartAddress > playlist.PlaylistStartAddress);
        Assert.NotEmpty(playlist.PlayItems);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("BD-A", "00000.mpls", "00003")]
    [InlineData("UHD-A", "00149.mpls", "00174")]
    public async Task ParseAsync_FeaturePlaylists_ReadsPlayItemsAndStreams(string discName, string filename, string expectedFirstClip)
    {
        var parser = CreateParser(Path.Combine(fixturesPath, discName, filename));

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var playlist = result.Value!;
        var first = playlist.PlayItems[0];

        Assert.Equal(expectedFirstClip, first.ClipId);
        Assert.Equal("M2TS", first.CodecId);
        Assert.True(first.OutTime > first.InTime);
        Assert.NotEmpty(first.StreamTable.VideoStreams);
        Assert.True(first.StreamTable.AudioStreams.Count > 0 || first.StreamTable.PresentationGraphicsStreams.Count > 0);
        Assert.All(playlist.SubPaths, subPath => Assert.Equal(subPath.SubPlayItemCount, subPath.SubPlayItems.Count));
    }

    [Fact]
    public async Task ParseAsync_UhdFeature_ReadsHevcStreamsAndMarks()
    {
        var parser = CreateParser(Path.Combine(fixturesPath, "UHD-A", "00149.mpls"));

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var playlist = result.Value!;
        var streams = playlist.PlayItems.SelectMany(item => item.StreamTable.VideoStreams).ToList();

        Assert.Contains(streams, stream => stream.CodingType == "HEVC Video");
        Assert.NotEmpty(playlist.Marks);
        Assert.All(playlist.Marks, mark => Assert.True(mark.PlayItemReference < playlist.PlayItems.Count));
        Assert.All(playlist.Marks, mark =>
        {
            Assert.Equal(MplsPlaylistMark.EntryMarkType, mark.MarkType);
            Assert.True(mark.IsEntryMark);
            Assert.False(mark.IsLinkMark);
        });
    }

    [Fact]
    public async Task ParseAsync_MvcPlaylist_ExposesDependentView()
    {
        var parser = CreateParser(Path.Combine(fixturesPath, "BD-3D", "00800.mpls"));

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var playlist = result.Value!;
        var playItem = Assert.Single(playlist.PlayItems);
        Assert.Equal("00300", playItem.ClipId);
        Assert.Equal("M2TS", playItem.CodecId);
        Assert.False(playItem.IsMultiAngle);
        Assert.Empty(playlist.SubPaths);

        Assert.NotNull(playlist.ExtensionData);
        Assert.Equal(120u, playlist.ExtensionData.Length);
        Assert.Collection(
            playlist.ExtensionData.Entries,
            entry =>
            {
                Assert.Equal(2, entry.TypeIdentifier);
                Assert.Equal(1, entry.VersionIdentifier);
                Assert.Equal("STN SS extension", entry.Name);
                Assert.True(entry.IsSupported);
            },
            entry =>
            {
                Assert.Equal(2, entry.TypeIdentifier);
                Assert.Equal(2, entry.VersionIdentifier);
                Assert.Equal("SubPath entries extension", entry.Name);
                Assert.True(entry.IsSupported);
            });

        var extensionSubPath = Assert.Single(playlist.ExtensionSubPaths);
        Assert.Equal(8, extensionSubPath.Type);
        var subPlayItem = Assert.Single(extensionSubPath.SubPlayItems);
        Assert.Equal("00301", subPlayItem.ClipId);
        Assert.Equal("M2TS", subPlayItem.CodecId);
        Assert.Equal(2, subPlayItem.StcId);
        Assert.Equal(0, subPlayItem.SyncPlayItemId);
        Assert.Equal(0x0007FFF8u, subPlayItem.SyncPresentationTimestamp);

        var relationship = Assert.Single(playlist.StereoVideoRelationships);
        Assert.Equal(MplsStereoVideoRelationship.ThreeDimensionalDependentView, relationship.RelationshipType);
        Assert.Equal("00300", relationship.BaseClipId);
        Assert.Equal("00301", relationship.DependentClipId);
        Assert.True(relationship.IsSsVideoSubPath);
        Assert.NotNull(relationship.DependentViewStream);
        Assert.Equal((ushort)0x1012, relationship.DependentViewStream.Pid);
        Assert.Equal(0x20, relationship.DependentViewStream.CodingTypeCode);
        Assert.Equal("MVC Video", relationship.DependentViewStream.CodingType);
        Assert.Equal(6, relationship.DependentViewStream.FormatCode.GetValueOrDefault());
        Assert.Equal(1, relationship.DependentViewStream.RateCode.GetValueOrDefault());
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ParseAsync_MvcPlaylist_ExposesAuthoredPlaylistMarks()
    {
        var parser = CreateParser(Path.Combine(fixturesPath, "BD-3D", "00800.mpls"));

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var playlist = result.Value!;
        var playItem = Assert.Single(playlist.PlayItems);

        Assert.Equal(15, playlist.Marks.Count);
        Assert.All(playlist.Marks, mark => Assert.True(mark.IsEntryMark));
        Assert.Equal(11262u, playItem.OutTime - playlist.Marks[^1].Time);
    }

    [Fact]
    public async Task ParseAsync_NonStereoPlaylist_HasNoStereoRelationships()
    {
        var parser = CreateParser(Path.Combine(fixturesPath, "UHD-A", "00149.mpls"));

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        Assert.Empty(result.Value!.ExtensionSubPaths);
        Assert.Empty(result.Value.StereoVideoRelationships);
    }

    [Fact]
    public async Task ParseAsync_UnsupportedExtensionEntry_PreservesBoundedMetadata()
    {
        var path = Path.Combine(fixturesPath, "BD-3D", "00800.mpls");
        var bytes = File.ReadAllBytes(path);
        int extensionDataStart = (int)ReadUInt32(bytes, 16);
        bytes[extensionDataStart + 12] = 0x7F;
        bytes[extensionDataStart + 13] = 0x01;
        bytes[extensionDataStart + 14] = 0x7F;
        bytes[extensionDataStart + 15] = 0x02;
        var parser = CreateParser(bytes);

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        var playlist = result.Value!;
        var unsupported = playlist.ExtensionData!.Entries[0];
        Assert.Equal(0x7F01, unsupported.TypeIdentifier);
        Assert.Equal(0x7F02, unsupported.VersionIdentifier);
        Assert.False(unsupported.IsSupported);
        Assert.NotNull(unsupported.RawDataHex);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MP023");
    }

    [Fact]
    public async Task ParseAsync_TruncatedExtensionDescriptor_ReportsDiagnostic()
    {
        var path = Path.Combine(fixturesPath, "BD-3D", "00800.mpls");
        var bytes = File.ReadAllBytes(path);
        int extensionDataStart = (int)ReadUInt32(bytes, 16);
        Array.Resize(ref bytes, extensionDataStart + 18);
        var parser = CreateParser(bytes);

        var result = await parser.ParseAsync();

        Assert.NotNull(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MP019");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MP020");
    }

    [Fact]
    public async Task ParseAsync_TruncatedHeader_ReturnsReaderAndParserDiagnostics()
    {
        var parser = CreateParser("MP"u8.ToArray());

        var result = await parser.ParseAsync();

        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PARTIAL_READ");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MP001");
    }

    [Fact]
    public async Task ParseAsync_ReusedParser_DoesNotLeakDiagnosticsBetweenCalls()
    {
        var validBytes = File.ReadAllBytes(Path.Combine(fixturesPath, "BD-A", "00000.mpls"));
        var source = new MutableOpticalDiscReader("MP"u8.ToArray());
        var parser = new MplsParser(source);

        var failed = await parser.ParseAsync();
        source.Data = validBytes;
        var succeeded = await parser.ParseAsync();

        Assert.NotEmpty(failed.Diagnostics);
        Assert.NotNull(succeeded.Value);
        Assert.DoesNotContain(succeeded.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ParseAsync_ConcurrentCalls_UseIndependentCursors()
    {
        var bytes = File.ReadAllBytes(Path.Combine(fixturesPath, "BD-A", "00000.mpls"));
        var parser = new MplsParser(new MemoryOpticalDiscReader(bytes));

        var results = await Task.WhenAll(
            parser.ParseAsync().AsTask(),
            parser.ParseAsync().AsTask());

        Assert.All(results, result => Assert.NotNull(result.Value));
        Assert.Equal(
            results[0].Value!.PlayItems.Select(item => (item.ClipId, item.InTime, item.OutTime)),
            results[1].Value!.PlayItems.Select(item => (item.ClipId, item.InTime, item.OutTime)));
        Assert.DoesNotContain(results.SelectMany(result => result.Diagnostics),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ParseAsync_AllFixtures_ParseWithoutCriticalErrors()
    {
        var files = Directory.GetFiles(fixturesPath, "*.mpls", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var parser = CreateParser(file);
            var result = await parser.ParseAsync();

            Assert.NotNull(result.Value);
            Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }
    }

    private static MplsParser CreateParser(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return CreateParser(bytes);
    }

    private static MplsParser CreateParser(byte[] bytes)
    {
        return new MplsParser(new MemoryOpticalDiscReader(bytes));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
        => ((uint)bytes[offset] << 24)
            | ((uint)bytes[offset + 1] << 16)
            | ((uint)bytes[offset + 2] << 8)
            | bytes[offset + 3];

    private sealed class MutableOpticalDiscReader(byte[] data) : IOpticalDiscReader
    {
        public byte[] Data { get; set; } = data;

        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int length)
        {
            if (offset < 0 || offset >= Data.Length)
            {
                return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
            }

            var availableLength = Math.Min(length, Data.Length - (int)offset);
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(
                new ReadOnlyMemory<byte>(Data, (int)offset, availableLength));
        }

        public ValueTask<long> GetLengthAsync() => ValueTask.FromResult((long)Data.Length);
    }
}
