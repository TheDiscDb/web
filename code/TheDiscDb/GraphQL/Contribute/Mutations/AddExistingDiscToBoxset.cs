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
    [Error(typeof(InvalidDiscPathException))]
    [Error(typeof(ExistingDiscAlreadyInBoxsetException))]
    [Error(typeof(InvalidBoxsetStatusException))]
    [Error(typeof(MismatchedReleaseSlugException))]
    [Authorize]
    public async Task<UserContributionBoxset> AddExistingDiscToBoxset(
        string boxsetId,
        string existingDiscPath,
        string discName,
        string discFormat,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var boxset = await LoadAndVerifyBoxset(database, userManager, boxsetId, cancellationToken);

        if (!boxset.Status.IsEditableByOwner())
        {
            throw new InvalidBoxsetStatusException(boxset.Status.ToString(), "modified");
        }

        var existingDisc = await ExistingDiscPathValidator.ValidateAsync(
            existingDiscPath,
            database,
            cancellationToken);

        // Boxset members must share the boxset's slug because the import-time resolver
        // (DataImportItemFactory.FindBoxsetDisc) uses boxset.Slug as the release directory
        // name when locating each member's release on disk. Block the mismatch up front.
        if (!string.Equals(existingDisc.ReleaseSlug, boxset.Slug, StringComparison.OrdinalIgnoreCase))
        {
            throw new MismatchedReleaseSlugException(
                boxset.Slug,
                existingDisc.ReleaseSlug,
                !string.IsNullOrWhiteSpace(discName) ? discName : "this disc");
        }

        // Check if this exact disc path is already in this boxset
        var alreadyInBoxset = boxset.Members.Any(m => m.ExistingDiscPath == existingDiscPath);
        if (alreadyInBoxset)
        {
            throw new ExistingDiscAlreadyInBoxsetException(existingDiscPath);
        }

        var maxSortOrder = boxset.Members.Any()
            ? boxset.Members.Max(m => m.SortOrder)
            : -1;

        var member = new UserContributionBoxsetMember
        {
            Boxset = boxset,
            ExistingDiscPath = existingDiscPath,
            ExistingDiscName = discName,
            ExistingDiscFormat = discFormat,
            SortOrder = maxSortOrder + 1,
        };

        boxset.Members.Add(member);
        await database.SaveChangesAsync(cancellationToken);

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);
        return boxset;
    }
}
