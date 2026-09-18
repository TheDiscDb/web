namespace TheDiscDb.UnitTests.DataImport;

using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.InputModels;

public class TitleItemHandlerTests
{
    [Test]
    public async Task BuildMissingItemWarning_IncludesTitleAndMappingContext()
    {
        var title = new Title
        {
            Index = 7,
            SourceFile = "00300.mpls",
            SegmentMap = "12,13",
            Duration = "1:23:45",
            Comment = "Main feature",
            Disc = new Disc
            {
                Name = "Disc 1",
                Slug = "main-feature",
                Format = "Blu-ray",
                ContentHash = "ABC123",
            },
            Item = new DiscItemReference
            {
                Type = "Episode",
                Title = "A Test Episode",
                Season = "2",
                Episode = "4",
            },
        };

        var updateTitle = new Title
        {
            Index = 8,
            SourceFile = "00007.m2ts",
            SegmentMap = "7",
            Duration = "0:00:10",
            Comment = "Different incoming title",
        };

        string warning = TitleItemHandler.BuildMissingItemWarning(title, updateTitle);

        await Assert.That(warning).Contains("Database title: index 7");
        await Assert.That(warning).Contains("Disc: 'Disc 1' (slug 'main-feature', format 'Blu-ray', content hash 'ABC123')");
        await Assert.That(warning).Contains("source file '00300.mpls'");
        await Assert.That(warning).Contains("segment map '12,13'");
        await Assert.That(warning).Contains("mapped item 'Episode, A Test Episode, season 2, episode 4'");
        await Assert.That(warning).Contains("Update title: index 8, source file '00007.m2ts'");
    }
}
