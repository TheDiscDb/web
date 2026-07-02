using Microsoft.AspNetCore.Authorization;
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
    public async Task<UserContributionDisc> DeleteDiscFromContribution(
        string contributionId,
        string discId,
        [Service] SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .Include(c => c.Boxset)
                .ThenInclude(b => b!.Members)
                .ThenInclude(m => m.Disc)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(userManager, contribution, contributionId, discId, cancellationToken: cancellationToken);

        var editableStatuses = new[]
        {
            UserContributionStatus.Pending,
            UserContributionStatus.Rejected,
            UserContributionStatus.ChangesRequested
        };

        if (!editableStatuses.Contains(contribution!.Status))
        {
            throw new InvalidContributionStatusException(contribution.Status.ToString(), "modified");
        }

        var decodedDiscId = this.idEncoder.Decode(discId);
        var disc = contribution.Discs.FirstOrDefault(d => d.Id == decodedDiscId);
        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        if (disc.Index is int removedDiscIndex)
        {
            foreach (var remainingDisc in contribution.Discs.Where(d => d.Id != disc.Id && d.Index is int index && index > removedDiscIndex))
            {
                remainingDisc.Index--;
            }
        }

        if (contribution.Boxset != null)
        {
            var member = contribution.Boxset.Members.FirstOrDefault(m => m.Disc?.Id == disc.Id);
            if (member != null)
            {
                var removedSortOrder = member.SortOrder;
                contribution.Boxset.Members.Remove(member);
                database.UserContributionBoxsetMembers.Remove(member);

                foreach (var remaining in contribution.Boxset.Members.Where(m => m.SortOrder > removedSortOrder))
                {
                    remaining.SortOrder--;
                }
            }
        }

        contribution.Discs.Remove(disc);
        database.UserContributionDiscs.Remove(disc);

        await database.SaveChangesAsync(cancellationToken);
        return disc;
    }
}
