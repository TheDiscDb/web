using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(AuthenticationException))]
    [Error(typeof(BoxsetNotFoundException))]
    [Error(typeof(DiscNotFoundException))]
    [Error(typeof(ContributionAlreadyInBoxsetException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Error(typeof(InvalidBoxsetStatusException))]
    [Authorize]
    public async Task<UserContributionBoxset> AddDiscToBoxset(
        string boxsetId,
        string discId,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(userManager);
        var boxset = await LoadAndVerifyBoxset(database, userManager, boxsetId, cancellationToken);

        if (boxset.Status != UserContributionStatus.Pending)
        {
            throw new InvalidBoxsetStatusException(boxset.Status.ToString(), "modified");
        }

        var decodedDiscId = this.idEncoder.Decode(discId);
        if (decodedDiscId == 0)
        {
            throw new InvalidIdException(discId, "Disc");
        }

        var disc = await database.UserContributionDiscs
            .Include(d => d.UserContribution)
            .FirstOrDefaultAsync(d => d.Id == decodedDiscId, cancellationToken);

        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        if (disc.UserContribution.UserId != userId)
        {
            throw new InvalidOwnershipException(discId, "Disc");
        }

        // Check if this disc is already in any boxset
        var alreadyInBoxset = await database.UserContributionBoxsetMembers
            .AnyAsync(m => EF.Property<int>(m, "DiscId") == decodedDiscId, cancellationToken);

        if (alreadyInBoxset)
        {
            throw new ContributionAlreadyInBoxsetException(discId);
        }

        var maxSortOrder = boxset.Members.Any()
            ? boxset.Members.Max(m => m.SortOrder)
            : -1;

        var member = new UserContributionBoxsetMember
        {
            Boxset = boxset,
            Disc = disc,
            SortOrder = maxSortOrder + 1,
        };

        boxset.Members.Add(member);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ContributionAlreadyInBoxsetException(discId);
        }

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);
        return boxset;
    }
}
