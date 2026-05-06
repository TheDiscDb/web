using TheDiscDb.Naming;

namespace TheDiscDb.UnitTests.Naming;

public class DefaultFileNameTemplatesTests
{
    [Test]
    public async Task KnownItemTypes_ContainsAllFiveTypes()
    {
        await Assert.That(DefaultFileNameTemplates.KnownItemTypes).Contains(ItemTypeNames.MainMovie);
        await Assert.That(DefaultFileNameTemplates.KnownItemTypes).Contains(ItemTypeNames.Episode);
        await Assert.That(DefaultFileNameTemplates.KnownItemTypes).Contains(ItemTypeNames.Extra);
        await Assert.That(DefaultFileNameTemplates.KnownItemTypes).Contains(ItemTypeNames.Trailer);
        await Assert.That(DefaultFileNameTemplates.KnownItemTypes).Contains(ItemTypeNames.DeletedScene);
    }

    [Test]
    [Arguments(ItemTypeNames.Other)]
    [Arguments(ItemTypeNames.Interview)]
    [Arguments(ItemTypeNames.Featurette)]
    [Arguments(ItemTypeNames.Scene)]
    [Arguments(ItemTypeNames.Music)]
    [Arguments(ItemTypeNames.Short)]
    public async Task KnownItemTypes_ContainsExtraSubTypes(string itemType)
    {
        await Assert.That(DefaultFileNameTemplates.KnownItemTypes).Contains(itemType);
        await Assert.That(DefaultFileNameTemplates.IsKnownItemType(itemType)).IsTrue();
        await Assert.That(DefaultFileNameTemplates.GetDefault(itemType)).IsEqualTo(DefaultFileNameTemplates.Extra);
    }

    [Test]
    public async Task IsKnownItemType_ReturnsTrueForKnown()
    {
        await Assert.That(DefaultFileNameTemplates.IsKnownItemType("MainMovie")).IsTrue();
        await Assert.That(DefaultFileNameTemplates.IsKnownItemType("mainmovie")).IsTrue();
    }

    [Test]
    public async Task IsKnownItemType_ReturnsFalseForUnknown()
    {
        await Assert.That(DefaultFileNameTemplates.IsKnownItemType("Bogus")).IsFalse();
        await Assert.That(DefaultFileNameTemplates.IsKnownItemType(null)).IsFalse();
        await Assert.That(DefaultFileNameTemplates.IsKnownItemType("")).IsFalse();
    }

    [Test]
    public async Task GetDefault_UnknownReturnsNull()
    {
        await Assert.That(DefaultFileNameTemplates.GetDefault("Unknown")).IsNull();
    }

    [Test]
    public async Task AllDefaults_AreValidTemplates()
    {
        foreach (var (itemType, template) in DefaultFileNameTemplates.All)
        {
            var result = NamingTemplate.Parse(template);
            await Assert.That(result.IsSuccess).IsTrue();
        }
    }

    [Test]
    public async Task MainMovie_FormatsExpectedFileName()
    {
        var ctx = new NamingContext
        {
            FullTitle = "The Matrix (1999)",
            Edition = "Theatrical",
            Resolution = "2160p",
        };

        var template = NamingTemplate.Parse(DefaultFileNameTemplates.MainMovie).Template!;
        var actual = template.Format(ctx);

        await Assert.That(actual).IsEqualTo("The Matrix (1999) [2160p].mkv");
    }

    [Test]
    public async Task Episode_FormatsExpectedFileName()
    {
        var ctx = new NamingContext
        {
            Title = "Severance",
            Year = "2022",
            FullTitle = "Severance (2022)",
            SeasonNumber = "01",
            EpisodeNumber = "03",
            EpisodeName = "In Perpetuity",
            Resolution = "1080p",
        };

        var template = NamingTemplate.Parse(DefaultFileNameTemplates.Episode).Template!;
        var actual = template.Format(ctx);

        await Assert.That(actual).IsEqualTo("Severance.S01.E03.In Perpetuity.mkv");
    }

    [Test]
    public async Task MainMovie_MissingEdition_TrimsAdjacentSpace()
    {
        var ctx = new NamingContext
        {
            FullTitle = "The Matrix (1999)",
            Resolution = "2160p",
        };

        var template = NamingTemplate.Parse("{fulltitle} - {edition} - {resolution}").Template!;
        var actual = template.Format(ctx);

        // Smart whitespace trimming removes one space adjacent to the missing
        // edition token; the resulting double-dash is left intentionally so the
        // user can spot a missing edition and either fill it in or customize.
        await Assert.That(actual).IsEqualTo("The Matrix (1999) - - 2160p");
    }
}
