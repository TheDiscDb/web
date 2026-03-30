using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Boxset;

public class BoxsetSlugValidation : IBoxsetValidation
{
    public string DisplayName => "Boxset Has Valid Slug";

    public Task<Result> Validate(UserContributionBoxset boxset, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(boxset.Slug))
        {
            return Task.FromResult(Result.Fail("The boxset must have a slug."));
        }

        if (boxset.Slug.Contains(' '))
        {
            return Task.FromResult(Result.Fail("The boxset slug must not contain spaces."));
        }

        return Task.FromResult(Result.Ok());
    }
}
