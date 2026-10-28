using MakeMkv;
using TheDiscDb.Import;
using TheDiscDb.ImportModels;

namespace TheDiscDb.UnitTests.Import;

public class DiscFileFinalizerTests
{
    private static DiscInfo BuildDiscInfoWithTwoSubtitles()
    {
        var title = new MakeMkv.Title
        {
            Index = 1,
            Playlist = "00001.mpls",
            SegmentMap = "1",
            Length = "2:00:00",
            DisplaySize = "30.0 GB",
            ChapterCount = 3,
        };

        title.Segments.Add(new Segment { Index = 0, Type = "Video", Resolution = "1080p" });
        title.Segments.Add(new Segment { Index = 1, Type = "Audio", Language = "English", LanguageCode = "eng", AudioType = "DTS-HD MA" });
        title.Segments.Add(new Segment { Index = 2, Type = "Subtitles", Language = "English", LanguageCode = "eng" });
        title.Segments.Add(new Segment { Index = 3, Type = "Subtitles", Language = "French", LanguageCode = "fra" });

        var discInfo = new DiscInfo { Name = "Test Disc" };
        discInfo.Titles.Add(title);
        return discInfo;
    }

    private static DiscFileItem BuildMainMovieItem(params SubtitleTrack[] subtitleTracks)
    {
        var item = new DiscFileItem
        {
            Title = "Movie",
            Type = "MainMovie",
            SourceFile = "00001.mpls",
            SegmentMap = "1",
            Duration = "2:00:00",
            Size = "30.0 GB",
        };

        foreach (var track in subtitleTracks)
        {
            item.SubtitleTrackNames.Add(track);
        }

        return item;
    }

    [Test]
    public async Task Map_SubtitleTracks_WriteDescriptionsToMatchingStreams()
    {
        var discInfo = BuildDiscInfoWithTwoSubtitles();
        var discFile = new DiscFile { Index = 1 };
        discFile.MainMovies.Add(BuildMainMovieItem(
            new SubtitleTrack(1, "English"),
            new SubtitleTrack(2, "French")));

        var disc = new TheDiscDb.InputModels.Disc();
        DiscFileFinalizer.Map(disc, discFile, discInfo);

        var subtitleTracks = disc.Titles.Single().Tracks
            .Where(t => string.Equals(t.Type, "Subtitles", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Index)
            .ToList();

        await Assert.That(subtitleTracks.Count).IsEqualTo(2);
        await Assert.That(subtitleTracks[0].Description).IsEqualTo("English");
        await Assert.That(subtitleTracks[1].Description).IsEqualTo("French");
    }

    [Test]
    public async Task Map_TrackNamesUsePerKindOrdinalsAcrossInterleavedStreams()
    {
        var discInfo = BuildDiscInfoWithTwoSubtitles();
        discInfo.Titles.Single().Segments.Add(new Segment
        {
            Index = 4,
            Type = "Audio",
            Language = "English",
            LanguageCode = "eng",
            AudioType = "Stereo",
        });
        var item = BuildMainMovieItem(new SubtitleTrack(2, "French captions"));
        item.AudioTrackNames.Add(new AudioTrack(2, "Director commentary"));
        var discFile = new DiscFile { Index = 1 };
        discFile.MainMovies.Add(item);
        var disc = new TheDiscDb.InputModels.Disc();

        DiscFileFinalizer.Map(disc, discFile, discInfo);

        var tracks = disc.Titles.Single().Tracks;
        await Assert.That(tracks.Single(t => t.Index == 4).Description).IsEqualTo("Director commentary");
        await Assert.That(tracks.Single(t => t.Index == 3).Description).IsEqualTo("French captions");
        await Assert.That(tracks.Single(t => t.Index == 1).Description).IsNull();
        await Assert.That(tracks.Single(t => t.Index == 2).Description).IsNull();
    }

    [Test]
    public async Task Map_SubtitleTrackIndexOutOfRange_Throws()
    {
        var discInfo = BuildDiscInfoWithTwoSubtitles();
        var discFile = new DiscFile { Index = 1 };
        // Index 3 does not exist (only two subtitle streams).
        discFile.MainMovies.Add(BuildMainMovieItem(new SubtitleTrack(3, "German")));

        var disc = new TheDiscDb.InputModels.Disc();

        await Assert.That(() => DiscFileFinalizer.Map(disc, discFile, discInfo))
            .Throws<Exception>();
    }
}