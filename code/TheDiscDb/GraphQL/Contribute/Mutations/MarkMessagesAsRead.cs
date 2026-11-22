using HotChocolate.Authorization;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Services;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(AuthenticationException))]
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<bool> MarkMessagesAsRead(
        string contributionId,
        IReadOnlyList<int>? messageIds,
        IMessageService messageService,
        CancellationToken cancellationToken)
    {
        var user = this.principal.Principal ?? throw new AuthenticationException("No user principal available.");
        await messageService.MarkContributionMessagesAsReadAsync(this.DecodeMessageThreadId(contributionId, "Contribution"), user, messageIds, cancellationToken);
        return true;
    }

    [Error(typeof(AuthenticationException))]
    [Error(typeof(BoxsetNotFoundException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<bool> MarkBoxsetMessagesAsRead(
        string boxsetId,
        IReadOnlyList<int>? messageIds,
        IMessageService messageService,
        CancellationToken cancellationToken)
    {
        var user = this.principal.Principal ?? throw new AuthenticationException("No user principal available.");
        await messageService.MarkBoxsetMessagesAsReadAsync(this.DecodeMessageThreadId(boxsetId, "Boxset"), user, messageIds, cancellationToken);
        return true;
    }

    private int DecodeMessageThreadId(string id, string type)
    {
        try
        {
            return this.idEncoder.Decode(id);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidIdException(id, type);
        }
    }
}