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
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Error(typeof(InvalidBoxsetStatusException))]
    [Authorize]
    public async Task<UserContributionBoxset> RemoveBoxsetMember(
        string boxsetId,
        int memberId,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var boxset = await LoadAndVerifyBoxset(database, userManager, boxsetId, cancellationToken);

        if (!boxset.Status.IsEditableByOwner())
        {
            throw new InvalidBoxsetStatusException(boxset.Status.ToString(), "modified");
        }

        var member = boxset.Members.FirstOrDefault(m => m.Id == memberId);
        if (member == null)
        {
            throw new InvalidIdException(memberId.ToString(), "BoxsetMember");
        }

        var removedSortOrder = member.SortOrder;
        boxset.Members.Remove(member);
        database.UserContributionBoxsetMembers.Remove(member);

        foreach (var remaining in boxset.Members.Where(m => m.SortOrder > removedSortOrder))
        {
            remaining.SortOrder--;
        }

        await database.SaveChangesAsync(cancellationToken);

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);
        return boxset;
    }
}
