using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(AuthenticationException))]
    [Authorize]
    public async Task<bool> MarkMessagesAsRead(
        string contributionId,
        SqlServerDataContext database,
        CancellationToken cancellationToken)
    {
        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        var decodedContributionId = this.idEncoder.Decode(contributionId);

        var unreadMessages = await database.UserMessages
            .Where(m => m.ContributionId == decodedContributionId &&
                        m.ToUserId == userId &&
                        !m.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var message in unreadMessages)
        {
            message.IsRead = true;
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
