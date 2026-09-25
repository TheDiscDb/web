using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscManifest.Presentation;

namespace TheDiscDb.OpticalDiscParsers.Tests;

public sealed class ManifestTitleSummaryTests
{
    [Fact]
    public void Build_SurfacesMainFeatureFirst_AmongManySmallTitles()
    {
        // Mirrors a feature disc shape: dozens of short menu/trailer
        // playlists (1-2 chapters each) plus a single 14-chapter main feature whose
        // SizeBytes already reflects the combined base + dependent-view clip size.
        var titles = new List<ManifestTitle>();
        for (int i = 0; i < 74; i++)
        {
            titles.Add(new ManifestTitle
            {
                Index = i,
                Source = new ManifestTitleSource { Kind = "blu-ray-playlist", Path = $"BDMV/PLAYLIST/{i:00000}.mpls" },
                DurationSeconds = 30 + i,
                ChapterCount = 1,
                SizeBytes = 50_000_000,
            });
        }

        var mainFeature = new ManifestTitle
        {
            Index = 74,
            Source = new ManifestTitleSource { Kind = "blu-ray-playlist", Path = "BDMV/PLAYLIST/00800.mpls" },
            DurationSeconds = 8478.761956,
            ChapterCount = 14,
            SizeBytes = 43_553_961_984,
            Segments = [new ManifestSegment { Index = 0, Clip = "00300", FilePath = "BDMV/STREAM/00300.m2ts" }],
            Stereoscopic3D = new ManifestStereoscopicView
            {
                RelationshipType = "3d-dependent-view",
                BaseClipId = "00300",
                DependentClipId = "00301",
            },
        };
        titles.Add(mainFeature);

        var rows = ManifestTitleSummaryBuilder.Build(titles);

        Assert.Equal(75, rows.Count);
        var first = rows[0];
        Assert.Equal("BDMV/PLAYLIST/00800.mpls", first.SourcePath);
        Assert.Equal(14, first.ChapterCount);
        Assert.True(first.Is3D);
        Assert.Equal("base 00300 \u2192 dependent 00301 (MVC)", first.StereoscopicSummary);
        Assert.Equal("2:21:18", first.DurationDisplay);
        Assert.Equal("40.56 GiB", first.SizeDisplay);
        Assert.Equal("00300", first.SegmentClipIds);
    }

    [Fact]
    public void Build_ReturnsEmpty_WhenNoTitles()
    {
        Assert.Empty(ManifestTitleSummaryBuilder.Build(null));
        Assert.Empty(ManifestTitleSummaryBuilder.Build([]));
    }

    [Fact]
    public void Build_OmitsStereoscopicSummary_ForNonThreeDTitles()
    {
        var titles = new[]
        {
            new ManifestTitle
            {
                Index = 0,
                Source = new ManifestTitleSource { Kind = "blu-ray-playlist", Path = "BDMV/PLAYLIST/00001.mpls" },
                DurationSeconds = 120,
                ChapterCount = 1,
                SizeBytes = 1_000_000,
            },
        };

        var rows = ManifestTitleSummaryBuilder.Build(titles);

        Assert.False(rows[0].Is3D);
        Assert.Null(rows[0].StereoscopicSummary);
        Assert.Equal("2:00", rows[0].DurationDisplay);
    }

    [Fact]
    public void ClipBuild_SortsBySizeDescendingAndFormatsFields()
    {
        var clips = new[]
        {
            new ManifestClip
            {
                ClipId = "00301",
                StreamPath = "BDMV/STREAM/00301.m2ts",
                SizeBytes = 20_000_000_000,
                DurationSeconds = 8478.76,
                Streams = [new ManifestStream { Index = 0, Type = "video", Codec = "MVC Video" }],
            },
            new ManifestClip
            {
                ClipId = "00300",
                StreamPath = "BDMV/STREAM/00300.m2ts",
                SizeBytes = 23_553_961_984,
                DurationSeconds = 8478.76,
                Streams =
                [
                    new ManifestStream { Index = 0, Type = "video", Codec = "AVC Video" },
                    new ManifestStream { Index = 1, Type = "audio", Codec = "DTS-HD Master Audio" },
                ],
            },
        };

        var rows = ManifestClipSummaryBuilder.Build(clips);

        Assert.Equal(2, rows.Count);
        Assert.Equal("00300", rows[0].ClipId);
        Assert.Equal(2, rows[0].StreamCount);
        Assert.Equal("00301", rows[1].ClipId);
        Assert.Equal(1, rows[1].StreamCount);
        Assert.NotNull(rows[0].SizeDisplay);
        Assert.NotNull(rows[0].DurationDisplay);
    }

    [Fact]
    public void ClipBuild_ReturnsEmpty_WhenNoClips()
    {
        Assert.Empty(ManifestClipSummaryBuilder.Build(null));
        Assert.Empty(ManifestClipSummaryBuilder.Build([]));
    }
}
