using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(AuthenticationException))]
    [Error(typeof(BoxsetNotFoundException))]
    [Error(typeof(DiscNotFoundException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Error(typeof(InvalidBoxsetStatusException))]
    [Authorize]
    public async Task<UserContributionBoxset> RemoveDiscFromBoxset(
        string boxsetId,
        string discId,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
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

        var member = boxset.Members.FirstOrDefault(m => m.Disc.Id == decodedDiscId);
        if (member == null)
        {
            throw new DiscNotFoundException(discId);
        }

        var removedSortOrder = member.SortOrder;
        boxset.Members.Remove(member);
        database.UserContributionBoxsetMembers.Remove(member);

        // Recompact sort orders
        foreach (var remaining in boxset.Members.Where(m => m.SortOrder > removedSortOrder))
        {
            remaining.SortOrder--;
        }

        await database.SaveChangesAsync(cancellationToken);

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);
        return boxset;
    }
}
