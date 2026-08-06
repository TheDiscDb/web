using FluentResults;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
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
    [Error(typeof(InvalidContributionStatusException))]
    [Authorize]
    public async Task<UserContributionDisc> UpdateDisc([Service] SqlServerDataContext database, string contributionId, string discId, string format, string name, string slug, PartialState? partial, UserManager<TheDiscDbUser> userManager, bool updatePartial = false, CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
        
        await EnsureOwnership(userManager, contribution, contributionId, discId, cancellationToken: cancellationToken);

        if (!EditableStatuses.Contains(contribution!.Status))
        {
            throw new InvalidContributionStatusException(contribution.Status.ToString());
        }

        int realDiscId = this.idEncoder.Decode(discId);
        var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);
        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        disc.Format = format;
        disc.Name = name;
        disc.Slug = slug;
        if (updatePartial)
        {
            disc.Partial = ValidateDeclaredPartial(partial, PartialStateTarget.Disc);
        }

        await database.SaveChangesAsync(cancellationToken);
        return disc;
    }
}
