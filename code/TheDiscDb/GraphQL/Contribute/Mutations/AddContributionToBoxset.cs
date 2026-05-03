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
    [Error(typeof(MismatchedReleaseSlugException))]
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

        if (!boxset.Status.IsEditableByOwner())
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

        // Boxset members must share the boxset's slug because the import-time resolver
        // (DataImportItemFactory.FindBoxsetDisc) uses boxset.Slug as the release directory
        // name when locating each member's release on disk. Block the mismatch up front so
        // the boxset doesn't end up with members that won't resolve on import.
        if (!string.Equals(disc.UserContribution.ReleaseSlug, boxset.Slug, StringComparison.OrdinalIgnoreCase))
        {
            throw new MismatchedReleaseSlugException(boxset.Slug, disc.UserContribution.ReleaseSlug ?? string.Empty, disc.UserContribution.Title ?? "this contribution");
        }

        // Check if this disc is already in any boxset
        var alreadyInBoxset = await database.UserContributionBoxsetMembers
            .AnyAsync(m => EF.Property<int?>(m, "DiscId") == decodedDiscId, cancellationToken);

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
