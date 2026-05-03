using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
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
    public async Task<UserContributionBoxset> ReorderBoxsetMembers(
        string boxsetId,
        List<int> memberIds,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var boxset = await LoadAndVerifyBoxset(database, userManager, boxsetId, cancellationToken);

        if (!boxset.Status.IsEditableByOwner())
        {
            throw new InvalidBoxsetStatusException(boxset.Status.ToString(), "modified");
        }

        // Reject duplicates
        if (memberIds.Distinct().Count() != memberIds.Count)
        {
            throw new InvalidIdException(string.Join(", ", memberIds), "Duplicate member IDs");
        }

        // Verify all provided IDs match the boxset's current members
        var currentMemberIds = boxset.Members.Select(m => m.Id).ToHashSet();
        var providedIds = memberIds.ToHashSet();

        if (!currentMemberIds.SetEquals(providedIds) || memberIds.Count != currentMemberIds.Count)
        {
            throw new InvalidIdException(string.Join(", ", memberIds), "Member list mismatch");
        }

        // Apply new order
        for (int i = 0; i < memberIds.Count; i++)
        {
            var member = boxset.Members.First(m => m.Id == memberIds[i]);
            member.SortOrder = i;
        }

        await database.SaveChangesAsync(cancellationToken);

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);
        return boxset;
    }
}
