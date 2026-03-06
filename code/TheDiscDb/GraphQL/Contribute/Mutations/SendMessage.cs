using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Authorize("Admin")]
    public async Task<ContributionHistory> SendAdminMessage(
        string contributionId,
        string message,
        SqlServerDataContext database,
        IContributionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 10_000)
        {
            throw new ArgumentException("Message must be between 1 and 10,000 characters.");
        }

        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken)
            ?? throw new ContributionNotFoundException(contributionId);

        await historyService.AddMessageAsync(contribution.Id, userId, message, ContributionHistoryType.AdminMessage, cancellationToken);

        return await database.ContributionHistory
            .Where(h => h.ContributionId == contribution.Id)
            .OrderByDescending(h => h.TimeStamp)
            .FirstAsync(cancellationToken);
    }

    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<ContributionHistory> SendUserMessage(
        string contributionId,
        string message,
        SqlServerDataContext database,
        IContributionHistoryService historyService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 10_000)
        {
            throw new ArgumentException("Message must be between 1 and 10,000 characters.");
        }

        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken)
            ?? throw new ContributionNotFoundException(contributionId);

        await EnsureOwnership(contribution, contributionId, cancellationToken: cancellationToken);

        await historyService.AddMessageAsync(contribution.Id, userId, message, ContributionHistoryType.UserMessage, cancellationToken);

        return await database.ContributionHistory
            .Where(h => h.ContributionId == contribution.Id)
            .OrderByDescending(h => h.TimeStamp)
            .FirstAsync(cancellationToken);
    }
}
