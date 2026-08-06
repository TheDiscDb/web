namespace TheDiscDb.UnitTests.Server.Services;

using System.Text.Json;
using TheDiscDb.Data.Import;
using TheDiscDb.Import;
using TheDiscDb.ImportModels;
using TheDiscDb.Services.Admin;
using TheDiscDb.Web.Data;

public class ContributionGeneratorPartialTests
{
    [Test]
    public async Task CreateReleaseOutput_WritesPartialToReleaseAndBoxsetSchemas()
    {
        var partial = new PartialState
        {
            Type = PartialStateType.MissingDiscs,
            Reason = PartialStateReason.OnlyOwnsSomeDiscs,
            Source = PartialStateSource.Declared,
        };
        var release = new ReleaseFile
        {
            Slug = "release",
            Title = "Release",
            Partial = partial,
        };

        var normal = ContributionGeneratorService.CreateReleaseOutput(release, ImportItemType.Movie);
        var boxset = ContributionGeneratorService.CreateReleaseOutput(release, ImportItemType.Boxset);
        var normalJson = JsonSerializer.Serialize(normal, JsonHelper.JsonOptions);
        var boxsetJson = JsonSerializer.Serialize(boxset, JsonHelper.JsonOptions);

        await Assert.That(normal).IsTypeOf<ReleaseFile>();
        await Assert.That(boxset).IsTypeOf<BoxSetReleaseFile>();
        await Assert.That(normalJson).Contains("\"Partial\"");
        await Assert.That(boxsetJson).Contains("\"Partial\"");
        await Assert.That(boxsetJson).Contains("\"MissingDiscs\"");
    }

    [Test]
    public async Task CreateDiscOutput_WritesPartialToDiscSchema()
    {
        var partial = new PartialState
        {
            Type = PartialStateType.Unidentified,
            Reason = PartialStateReason.LogsOnlyForOthersToComplete,
            Source = PartialStateSource.Declared,
        };
        var stagingDisc = new UserContributionDisc
        {
            ContentHash = "ABC",
            Name = "Disc 1",
            Slug = "disc01",
            Partial = partial,
        };

        var output = ContributionGeneratorService.CreateDiscOutput(
            stagingDisc,
            discIndex: 1,
            discFormat: "Blu-ray");
        var json = JsonSerializer.Serialize(output, JsonHelper.JsonOptions);

        await Assert.That(output.Partial).IsEqualTo(partial);
        await Assert.That(json).Contains("\"Partial\"");
        await Assert.That(json).Contains("\"Unidentified\"");
    }
}
