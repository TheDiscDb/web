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

        return await messageService.SendAdminMessageAsync(contribution.Id, userId, contribution.UserId, message, cancellationToken);
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

        if (user.IsInRole(DefaultRoles.Administrator))
        {
            return await messageService.SendAdminMessageAsync(contribution.Id, userId, contribution.UserId, message, cancellationToken);
        }

        await EnsureOwnership(userManager, contribution, contributionId, cancellationToken: cancellationToken);

        return await messageService.SendUserMessageAsync(contribution.Id, userId, message, cancellationToken);
    }

    [Error(typeof(BoxsetNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Authorize("Admin")]
    public async Task<UserMessage> SendAdminBoxsetMessage(
        string boxsetId,
        string message,
        SqlServerDataContext database,
        IMessageService messageService,
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

        var decodedBoxsetId = this.idEncoder.Decode(boxsetId);
        if (decodedBoxsetId == 0)
        {
            throw new InvalidIdException(boxsetId, "Boxset");
        }

        var boxset = await database.UserContributionBoxsets
            .FirstOrDefaultAsync(b => b.Id == decodedBoxsetId, cancellationToken)
            ?? throw new BoxsetNotFoundException(boxsetId);

        return await messageService.SendAdminBoxsetMessageAsync(boxset.Id, userId, boxset.UserId, message, cancellationToken);
    }

    [Error(typeof(BoxsetNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserMessage> SendBoxsetUserMessage(
        string boxsetId,
        string message,
        SqlServerDataContext database,
        IMessageService messageService,
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

        var decodedBoxsetId = this.idEncoder.Decode(boxsetId);
        if (decodedBoxsetId == 0)
        {
            throw new InvalidIdException(boxsetId, "Boxset");
        }

        var boxset = await database.UserContributionBoxsets
            .FirstOrDefaultAsync(b => b.Id == decodedBoxsetId, cancellationToken)
            ?? throw new BoxsetNotFoundException(boxsetId);

        if (user.IsInRole(DefaultRoles.Administrator))
        {
            return await messageService.SendAdminBoxsetMessageAsync(boxset.Id, userId, boxset.UserId, message, cancellationToken);
        }

        if (boxset.UserId != userId)
        {
            throw new InvalidOwnershipException(boxsetId, "Boxset");
        }

        return await messageService.SendUserBoxsetMessageAsync(boxset.Id, userId, message, cancellationToken);
    }
}
