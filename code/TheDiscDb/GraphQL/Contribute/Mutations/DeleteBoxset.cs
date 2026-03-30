using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(AuthenticationException))]
    [Error(typeof(BoxsetNotFoundException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Error(typeof(InvalidBoxsetStatusException))]
    [Authorize]
    public async Task<UserContributionBoxset> DeleteBoxset(
        string boxsetId,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var boxset = await LoadAndVerifyBoxset(database, userManager, boxsetId, cancellationToken);

        var deletableStatuses = new[]
        {
            UserContributionStatus.Pending,
            UserContributionStatus.Rejected,
            UserContributionStatus.ChangesRequested
        };

        if (!deletableStatuses.Contains(boxset.Status))
        {
            throw new InvalidBoxsetStatusException(boxset.Status.ToString(), "deleted");
        }

        database.UserContributionBoxsets.Remove(boxset);
        await database.SaveChangesAsync(cancellationToken);

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);
        return boxset;
    }
}
