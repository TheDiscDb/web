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
    public async Task<UserMessage> SendAdminMessage(
        string contributionId,
        string message,
        SqlServerDataContext database,
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

        var userMessage = new UserMessage
        {
            ContributionId = contribution.Id,
            FromUserId = userId,
            ToUserId = contribution.UserId,
            Message = message,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.AdminMessage
        };

        database.UserMessages.Add(userMessage);
        await database.SaveChangesAsync(cancellationToken);

        return userMessage;
    }

    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserMessage> SendUserMessage(
        string contributionId,
        string message,
        SqlServerDataContext database,
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

        // Find an admin to set as ToUserId — use the most recent admin who messaged this contribution,
        // or fall back to empty (admins will see it via contribution queries)
        var lastAdminId = await database.UserMessages
            .Where(m => m.ContributionId == contribution.Id && m.Type == UserMessageType.AdminMessage)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.FromUserId)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var userMessage = new UserMessage
        {
            ContributionId = contribution.Id,
            FromUserId = userId,
            ToUserId = lastAdminId,
            Message = message,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.UserMessage
        };

        database.UserMessages.Add(userMessage);
        await database.SaveChangesAsync(cancellationToken);

        return userMessage;
    }
}
