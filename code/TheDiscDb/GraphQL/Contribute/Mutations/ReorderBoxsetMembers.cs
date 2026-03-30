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
        List<string> discIds,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var boxset = await LoadAndVerifyBoxset(database, userManager, boxsetId, cancellationToken);

        if (boxset.Status != UserContributionStatus.Pending)
        {
            throw new InvalidBoxsetStatusException(boxset.Status.ToString(), "modified");
        }

        // Decode all disc IDs
        var decodedIds = discIds.Select(id =>
        {
            var decoded = this.idEncoder.Decode(id);
            if (decoded == 0) throw new InvalidIdException(id, "Disc");
            return decoded;
        }).ToList();

        // Reject duplicates
        if (decodedIds.Distinct().Count() != decodedIds.Count)
        {
            throw new InvalidIdException(string.Join(", ", discIds), "Duplicate disc IDs");
        }

        // Verify all provided IDs match the boxset's current members
        var memberDiscIds = boxset.Members.Select(m => m.Disc.Id).ToHashSet();
        var providedIds = decodedIds.ToHashSet();

        if (!memberDiscIds.SetEquals(providedIds) || decodedIds.Count != memberDiscIds.Count)
        {
            throw new InvalidIdException(string.Join(", ", discIds), "Disc list mismatch");
        }

        // Apply new order
        for (int i = 0; i < decodedIds.Count; i++)
        {
            var member = boxset.Members.First(m => m.Disc.Id == decodedIds[i]);
            member.SortOrder = i;
        }

        await database.SaveChangesAsync(cancellationToken);

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);
        return boxset;
    }
}
