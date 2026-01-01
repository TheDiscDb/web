using FluentResults;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(DiscNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserContributionDisc> UpdateDisc([Service] SqlServerDataContext database, string contributionId, string discId, string format, string name, string slug, CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
        
        await EnsureOwnership(contribution, contributionId, discId, itemId: null, cancellationToken);

        int realDiscId = this.idEncoder.Decode(discId);
        var disc = contribution!.Discs.FirstOrDefault(d => d.Id == realDiscId);
        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        disc.Format = format;
        disc.Name = name;
        disc.Slug = slug;

        await database.SaveChangesAsync(cancellationToken);
        return disc;
    }
}
