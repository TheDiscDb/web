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

        // Validate disc path format and parse components
        string mediaType, externalId, releaseSlug, discSlug;
        try
        {
            (mediaType, externalId, releaseSlug, discSlug) = UserContributionDisc.ParseDiscPath(existingDiscPath);
        }
        catch (ArgumentException)
        {
            throw new InvalidDiscPathException(existingDiscPath);
        }

        // Boxset members must share the boxset's slug because the import-time resolver
        // (DataImportItemFactory.FindBoxsetDisc) uses boxset.Slug as the release directory
        // name when locating each member's release on disk. Block the mismatch up front.
        if (!string.Equals(releaseSlug, boxset.Slug, StringComparison.OrdinalIgnoreCase))
        {
            throw new MismatchedReleaseSlugException(boxset.Slug, releaseSlug, !string.IsNullOrWhiteSpace(discName) ? discName : "this disc");
        }

        // The path's last segment is either the disc's Slug, or — when the disc has no
        // slug — its Index (matching DiscExtensions.SlugOrIndex used elsewhere on the
        // site). Translate to a parsed-int once so EF can use a constant in the query.
        bool discKeyIsIndex = int.TryParse(discSlug, out var discIndex);

        // Verify the disc actually exists in the database. Match by slug first; if the
        // path key is purely numeric we also accept it as the disc's Index (only when
        // Slug is null/empty so we don't shadow a legitimate "1"/"2" slug).
        var discExists = await database.Discs
            .AnyAsync(d =>
                d.Release != null &&
                d.Release.Slug == releaseSlug &&
                d.Release.MediaItem != null &&
                d.Release.MediaItem.Type == mediaType &&
                d.Release.MediaItem.Externalids.Tmdb == externalId &&
                (d.Slug == discSlug ||
                    (discKeyIsIndex && (d.Slug == null || d.Slug == "") && d.Index == discIndex)),
                cancellationToken);

        if (!discExists)
        {
            throw new InvalidDiscPathException(existingDiscPath);
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
