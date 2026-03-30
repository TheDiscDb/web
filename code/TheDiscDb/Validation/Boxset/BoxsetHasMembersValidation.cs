using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Boxset;

public class BoxsetHasMembersValidation : IBoxsetValidation
{
    public string DisplayName => "Boxset Has Members";

    public Task<Result> Validate(UserContributionBoxset boxset, CancellationToken cancellationToken)
    {
        if (boxset.Members == null || boxset.Members.Count == 0)
        {
            return Task.FromResult(Result.Fail("The boxset must have at least one member contribution."));
        }

        return Task.FromResult(Result.Ok());
    }
}
