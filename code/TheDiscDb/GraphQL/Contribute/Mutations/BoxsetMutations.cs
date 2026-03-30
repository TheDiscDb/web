using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    private string GetUserId(UserManager<TheDiscDbUser> userManager)
    {
        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        return userId;
    }

    private async Task<UserContributionBoxset> LoadAndVerifyBoxset(
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        string boxsetId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(userManager);
        var decodedId = this.idEncoder.Decode(boxsetId);
        if (decodedId == 0)
        {
            throw new InvalidIdException(boxsetId, "Boxset");
        }

        var boxset = await database.UserContributionBoxsets
            .Include(b => b.Members)
                .ThenInclude(m => m.Disc)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == decodedId, cancellationToken);

        if (boxset == null)
        {
            throw new BoxsetNotFoundException(boxsetId);
        }

        if (boxset.UserId != userId)
        {
            throw new InvalidOwnershipException(boxsetId, "Boxset");
        }

        return boxset;
    }
}
