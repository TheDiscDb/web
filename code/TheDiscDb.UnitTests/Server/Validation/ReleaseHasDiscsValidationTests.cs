using TheDiscDb.Validation.Contribution;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.Server.Validation;

public class ReleaseHasDiscsValidationTests
{
    private readonly ReleaseHasDiscsValidation validator = new();

    [Test]
    public async Task Validate_NullDiscs_Fails()
    {
        var contribution = new UserContribution { Discs = null! };

        var result = await validator.Validate(contribution, CancellationToken.None);

        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Errors.First().Message).Contains("at least one disc");
    }

    [Test]
    public async Task Validate_EmptyDiscs_Fails()
    {
        var contribution = new UserContribution();

        var result = await validator.Validate(contribution, CancellationToken.None);

        await Assert.That(result.IsFailed).IsTrue();
    }

    [Test]
    public async Task Validate_WithDiscs_Passes()
    {
        var contribution = new UserContribution();
        contribution.Discs.Add(new UserContributionDisc { Slug = "disc-1" });

        var result = await validator.Validate(contribution, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Validate_MultipleDiscs_Passes()
    {
        var contribution = new UserContribution();
        contribution.Discs.Add(new UserContributionDisc { Slug = "disc-1" });
        contribution.Discs.Add(new UserContributionDisc { Slug = "disc-2" });

        var result = await validator.Validate(contribution, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task DisplayName_IsCorrect()
    {
        await Assert.That(validator.DisplayName).IsEqualTo("Release Has Discs");
    }
}
