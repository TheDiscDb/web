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
}
