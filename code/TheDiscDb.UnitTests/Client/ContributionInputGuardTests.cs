using TheDiscDb.Client.Pages.Contribute;

namespace TheDiscDb.UnitTests.Client;

public class ContributionInputGuardTests
{
    [Test]
    public async Task GetNamingSuggestion_TitleAtStart_RemovesTitleAndSeparators()
    {
        var result = ContributionInputGuard.GetNamingSuggestion(
            "Star Wars",
            "Star Wars - Special Edition Steelbook",
            "star-wars-special-edition-steelbook",
            title => title.Slugify());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.SuggestedName).IsEqualTo("Special Edition Steelbook");
        await Assert.That(result.SuggestedSlug).IsEqualTo("special-edition-steelbook");
    }

    [Test]
    public async Task GetNamingSuggestion_TitleInMiddle_IgnoresCaseAndPunctuation()
    {
        var result = ContributionInputGuard.GetNamingSuggestion(
            "Star Wars",
            "Special Edition: STAR.WARS Steelbook",
            "special-edition-star-wars-steelbook",
            title => title.Slugify());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.SuggestedName).IsEqualTo("Special Edition Steelbook");
    }

    [Test]
    public async Task GetNamingSuggestion_TitleAtEnd_RemovesTrailingSeparator()
    {
        var result = ContributionInputGuard.GetNamingSuggestion(
            "Star Wars",
            "Special Edition - Star Wars",
            "special-edition-star-wars",
            title => title.Slugify());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.SuggestedName).IsEqualTo("Special Edition");
    }

    [Test]
    public async Task GetNamingSuggestion_TitleOnlyInSlug_PreservesEnteredName()
    {
        var result = ContributionInputGuard.GetNamingSuggestion(
            "Star Wars",
            "Special Edition",
            "star-wars-special-edition",
            title => title.Slugify());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.SuggestedName).IsEqualTo("Special Edition");
        await Assert.That(result.SuggestedSlug).IsEqualTo("special-edition");
    }

    [Test]
    [Arguments("Dungeons & Dragons", "dungeons-and-dragons-steelbook")]
    [Arguments("Five Nights at Freddy's", "five-nights-at-freddys-steelbook")]
    public async Task GetNamingSuggestion_TitleOnlyInSlug_UsesSlugNormalization(
        string mediaTitle,
        string enteredSlug)
    {
        var result = ContributionInputGuard.GetNamingSuggestion(
            mediaTitle,
            "Steelbook",
            enteredSlug,
            title => title.Slugify());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.SuggestedName).IsEqualTo("Steelbook");
        await Assert.That(result.SuggestedSlug).IsEqualTo("steelbook");
    }

    [Test]
    public async Task GetNamingSuggestion_TitleIsEntireName_ReturnsWarningWithoutApplyValues()
    {
        var result = ContributionInputGuard.GetNamingSuggestion(
            "Star Wars",
            "Star Wars",
            "star-wars",
            title => title.Slugify());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.CanApply).IsFalse();
    }

    [Test]
    public async Task GetNamingSuggestion_PartialWordMatch_ReturnsNull()
    {
        var result = ContributionInputGuard.GetNamingSuggestion(
            "Star Wars",
            "Star Warships Edition",
            "star-warships-edition",
            title => title.Slugify());

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetNamingSuggestion_ReleaseSlugFactory_PreservesYearConvention()
    {
        var result = ContributionInputGuard.GetNamingSuggestion(
            "Star Wars",
            "Star Wars Steelbook",
            "2024-star-wars-steelbook",
            title => $"2024-{title.Slugify()}");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.SuggestedSlug).IsEqualTo("2024-steelbook");
    }

    [Test]
    [Arguments("09", false, "9")]
    [Arguments("000", false, "0")]
    [Arguments("0", false, "0")]
    [Arguments("01-02", true, "1-2")]
    [Arguments("1-02", true, "1-2")]
    [Arguments("special", false, "special")]
    [Arguments("1A", true, "1A")]
    public async Task NormalizeNumber_Value_ReturnsExpected(
        string value,
        bool allowRange,
        string expected)
    {
        string? result = ContributionInputGuard.NormalizeNumber(value, allowRange);

        await Assert.That(result).IsEqualTo(expected);
    }
}
