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
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Error(typeof(InvalidDiscPathException))]
    [Authorize]
    public async Task<UserContributionDisc> CreateDisc(string contributionId, string contentHash, string format, string name, string slug, [Service] SqlServerDataContext database, UserManager<TheDiscDbUser> userManager, string? existingDiscPath = null, CancellationToken cancellationToken = default)
    {
        var disc = new UserContributionDisc
        {
            ContentHash = contentHash,
            Format = format,
            Name = name,
            Slug = slug,
            ExistingDiscPath = existingDiscPath ?? ""
        };

        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .Include(c => c.Boxset)
                .ThenInclude(b => b!.Members)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(userManager, contribution, contributionId, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(existingDiscPath))
        {
            await ValidateExistingDiscPath(existingDiscPath, database, cancellationToken);
        }

        var existingDisc = contribution?.Discs.FirstOrDefault(d => d.ContentHash == disc.ContentHash);
        if (existingDisc != null)
        {
            existingDisc.Format = format;
            existingDisc.Name = name;
            existingDisc.Slug = slug;
            existingDisc.ExistingDiscPath = existingDiscPath ?? "";
            await database.SaveChangesAsync(cancellationToken);
            return existingDisc;
        }
        else
        {
            // Normalize any existing null indices before computing max
            var existingDiscs = contribution!.Discs
                .OrderBy(d => d.Index ?? int.MaxValue)
                .ThenBy(d => d.Id)
                .ToList();

            for (int i = 0; i < existingDiscs.Count; i++)
            {
                if (existingDiscs[i].Index == null)
                {
                    existingDiscs[i].Index = i + 1;
                }
            }

            int maxIndex = existingDiscs.Any()
                ? existingDiscs.Max(d => d.Index!.Value)
                : 0;
            disc.Index = maxIndex + 1;
            contribution.Discs.Add(disc);

            // If this contribution is part of a boxset (and the boxset is still editable),
            // auto-add the new disc as a boxset member at the end of the existing list.
            // Same-transaction insert keeps the disc + member in sync.
            if (contribution.Boxset != null && contribution.Boxset.Status.IsEditableByOwner())
            {
                int maxSortOrder = contribution.Boxset.Members.Any()
                    ? contribution.Boxset.Members.Max(m => m.SortOrder)
                    : -1;

                contribution.Boxset.Members.Add(new UserContributionBoxsetMember
                {
                    Boxset = contribution.Boxset,
                    Disc = disc,
                    SortOrder = maxSortOrder + 1,
                });
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        return disc;
    }

    private static async Task ValidateExistingDiscPath(string existingDiscPath, SqlServerDataContext database, CancellationToken cancellationToken)
    {
        string mediaType;
        string externalId;
        string releaseSlug;
        string discSlug;

        try
        {
            (mediaType, externalId, releaseSlug, discSlug) = UserContributionDisc.ParseDiscPath(existingDiscPath);
        }
        catch (ArgumentException)
        {
            throw new InvalidDiscPathException(existingDiscPath);
        }

        var discKeyIsIndex = int.TryParse(discSlug, out var discIndex);
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
    }
}
