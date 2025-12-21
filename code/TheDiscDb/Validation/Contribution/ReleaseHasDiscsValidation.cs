using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Contribution;

public class ReleaseHasDiscsValidation : IContributionValidation
{
    public string DisplayName => "Release Has Discs";

    public Task<Result> Validate(UserContribution contribution, CancellationToken cancellationToken)
    {
        if (contribution.Discs == null || contribution.Discs.Count == 0)
        {
            return Task.FromResult(Result.Fail("The release must have at least one disc."));
        }

        return Task.FromResult(Result.Ok());
    }
}
