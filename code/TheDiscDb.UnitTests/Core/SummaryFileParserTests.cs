using TheDiscDb;

namespace TheDiscDb.UnitTests.Core;

public class SummaryFileParserTests
{
    private static string MakeItem(string name, string type) =>
        string.Join(System.Environment.NewLine, new[]
        {
            $"Name: {name}",
            $"Source file name: {name}.m2ts",
            "Duration: 0:10:00",
            "Size: 1.0 GB",
            "Segment map: 100",
            $"Type: {type}",
            $"File name: {name}.mkv",
        });

    [Test]
    [Arguments("Other", "Others")]
    [Arguments("Interview", "Interviews")]
    [Arguments("Featurette", "Featurettes")]
    [Arguments("Scene", "Scenes")]
    [Arguments("Music", "Musics")]
    [Arguments("Short", "Shorts")]
    public async Task Categorize_ExtraSubType_RoutesToOwnBucket(string type, string bucketName)
    {
        var input = MakeItem("Title", type);

        var disc = SummaryFileParser.ParseSingleDisc(input);

        var bucket = typeof(TheDiscDb.ImportModels.DiscFile)
            .GetProperty(bucketName)!
            .GetValue(disc) as System.Collections.IEnumerable;

        await Assert.That(bucket).IsNotNull();
        await Assert.That(bucket!.Cast<object>().Count()).IsEqualTo(1);
        await Assert.That(disc.Unknown).IsEmpty();
        await Assert.That(disc.Extras).IsEmpty();
    }

    [Test]
    public async Task Categorize_OriginalTypesStillRouted()
    {
        var input = string.Join(System.Environment.NewLine + System.Environment.NewLine, new[]
        {
            MakeItem("Movie",  "MainMovie"),
            MakeItem("BTS",    "Extra"),
            MakeItem("S01E01", "Episode"),
            MakeItem("Promo",  "Trailer"),
            MakeItem("Cut",    "DeletedScene"),
        });

        var disc = SummaryFileParser.ParseSingleDisc(input);

        await Assert.That(disc.MainMovies).Count().IsEqualTo(1);
        await Assert.That(disc.Extras).Count().IsEqualTo(1);
        await Assert.That(disc.Episodes).Count().IsEqualTo(1);
        await Assert.That(disc.Trailers).Count().IsEqualTo(1);
        await Assert.That(disc.DeletedScenes).Count().IsEqualTo(1);
        await Assert.That(disc.Others).IsEmpty();
        await Assert.That(disc.Interviews).IsEmpty();
        await Assert.That(disc.Featurettes).IsEmpty();
        await Assert.That(disc.Scenes).IsEmpty();
        await Assert.That(disc.Musics).IsEmpty();
        await Assert.That(disc.Shorts).IsEmpty();
    }

    private static string MakeItemWithTracks(params string[] trackLines) =>
        string.Join(System.Environment.NewLine, new[]
        {
            "Name: Feature",
            "Source file name: Feature.m2ts",
            "Duration: 2:00:00",
            "Size: 30.0 GB",
            "Segment map: 100",
        }
        .Concat(trackLines)
        .Concat(new[]
        {
            "Type: MainMovie",
            "File name: Feature.mkv",
        }));

    [Test]
    public async Task Parse_SubtitleTracks_PopulatedAlongsideAudio()
    {
        var input = MakeItemWithTracks(
            "AudioTrack[1]: English 5.1",
            "AudioTrack[2]: Director's Commentary",
            "SubtitleTrack[1]: English",
            "SubtitleTrack[2]: English (SDH)");

        var item = SummaryFileParser.Parse(input).Single();

        await Assert.That(item.AudioTrackNames.Count).IsEqualTo(2);
        await Assert.That(item.SubtitleTrackNames.Count).IsEqualTo(2);

        var subs = item.SubtitleTrackNames.OrderBy(s => s.Index).ToList();
        await Assert.That(subs[0].Index).IsEqualTo(1);
        await Assert.That(subs[0].Name).IsEqualTo("English");
        await Assert.That(subs[1].Index).IsEqualTo(2);
        await Assert.That(subs[1].Name).IsEqualTo("English (SDH)");
    }

    [Test]
    public async Task Parse_AudioTrack_BackCompatUnchanged()
    {
        var input = MakeItemWithTracks("AudioTrack[2]: Director's Commentary");

        var item = SummaryFileParser.Parse(input).Single();

        var audio = item.AudioTrackNames.Single();
        await Assert.That(audio.Index).IsEqualTo(2);
        await Assert.That(audio.Name).IsEqualTo("Director's Commentary");
        await Assert.That(item.SubtitleTrackNames).IsEmpty();
    }

    [Test]
    public async Task Parse_StripsTrailingCommentFromTrackLabels()
    {
        var input = MakeItemWithTracks(
            "AudioTrack[1]: English # DTS-HD MA · eng",
            "SubtitleTrack[1]: English # subtitles · eng");

        var item = SummaryFileParser.Parse(input).Single();

        await Assert.That(item.AudioTrackNames.Single().Name).IsEqualTo("English");
        await Assert.That(item.SubtitleTrackNames.Single().Name).IsEqualTo("English");
    }
}
