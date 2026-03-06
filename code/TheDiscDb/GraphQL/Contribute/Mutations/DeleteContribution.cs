using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Error(typeof(InvalidContributionStatusException))]
    [Authorize]
    public async Task<UserContribution> DeleteContribution(string contributionId, SqlServerDataContext database, IContributionHistoryService historyService, CancellationToken cancellationToken)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
                .ThenInclude(d => d.Items)
            .Include(c => c.HashItems)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(contribution, contributionId, cancellationToken: cancellationToken);

        var deletableStatuses = new[]
        {
            UserContributionStatus.Pending,
            UserContributionStatus.Rejected,
            UserContributionStatus.ChangesRequested
        };

        if (!deletableStatuses.Contains(contribution!.Status))
        {
            throw new InvalidContributionStatusException(contribution.Status.ToString());
        }

        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user) ?? throw new AuthenticationException("UserId not found");

        await historyService.RecordDeletedAsync(contribution!.Id, userId, cancellationToken);

        database.UserContributions.Remove(contribution);
        await database.SaveChangesAsync(cancellationToken);

        return contribution;
    }
}
