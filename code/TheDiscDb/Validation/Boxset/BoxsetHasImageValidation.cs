using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Boxset;

public class BoxsetHasImageValidation : IBoxsetValidation
{
    public string DisplayName => "Boxset Has Images";

    public Task<Result> Validate(UserContributionBoxset boxset, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(boxset.FrontImageUrl))
        {
            return Task.FromResult(Result.Fail("The boxset must have a front image."));
        }

        return Task.FromResult(Result.Ok());
    }
}
