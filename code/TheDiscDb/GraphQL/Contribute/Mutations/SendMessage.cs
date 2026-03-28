using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
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
        IMessageService messageService,
        IContributionNotificationService notificationService,
        UserManager<TheDiscDbUser> userManager,
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

        var result = await messageService.SendAdminMessageAsync(contribution.Id, userId, contribution.UserId, message, cancellationToken);

        try
        {
            var recipient = await userManager.FindByIdAsync(contribution.UserId);
            await notificationService.NotifyMessageFromAdminAsync(contribution, message, recipient?.Email);
        }
        catch (Exception) { /* non-blocking — logged by service */ }

        return result;
    }

    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserMessage> SendUserMessage(
        string contributionId,
        string message,
        SqlServerDataContext database,
        IMessageService messageService,
        IContributionNotificationService notificationService,
        UserManager<TheDiscDbUser> userManager,
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

        await EnsureOwnership(userManager, contribution, contributionId, cancellationToken: cancellationToken);

        var result = await messageService.SendUserMessageAsync(contribution.Id, userId, message, cancellationToken);

        try
        {
            var dbUser = await userManager.FindByIdAsync(userId);
            await notificationService.NotifyMessageFromUserAsync(contribution, message, dbUser?.UserName, dbUser?.Email);
        }
        catch (Exception) { /* non-blocking — logged by service */ }

        return result;
    }
}
