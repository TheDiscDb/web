using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(DiscNotFoundException))]
    [Error(typeof(DiscItemNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserContributionDiscItem> DeleteItemFromDisc(string contributionId, string discId, string itemId, SqlServerDataContext database, CancellationToken cancellationToken)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
                .ThenInclude(d => d.Items)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(contribution, contributionId, discId, itemId, cancellationToken);

        int realDiscId = this.idEncoder.Decode(discId);
        var disc = contribution!.Discs.FirstOrDefault(d => d.Id == realDiscId);

        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        int realItemId = this.idEncoder.Decode(itemId);
        var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

        if (item == null)
        {
            throw new DiscItemNotFoundException(itemId);
        }

        disc.Items.Remove(item);
        database.UserContributionDiscItems.Remove(item);

        await database.SaveChangesAsync(cancellationToken);

        return item;
    }
}
