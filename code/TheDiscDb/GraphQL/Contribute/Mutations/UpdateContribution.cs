using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserContribution> UpdateContribution([Service] SqlServerDataContext database, string contributionId,
        UserContributionStatus? status,
        string? mediaType,
        string? asin,
        string? upc,
        //string? frontImageUrl, 
        //string? backImageUrl, 
        string? releaseSlug,
        string? locale,
        string? regionCode,
        //string? title, 
        //string? year, 
        //string? titleSlug, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contributionId))
        {
            throw new InvalidIdException("NULL", "Contribution");
        }

        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(contribution, contributionId, cancellationToken: cancellationToken);

        bool changed = false;
        if (status.HasValue && status.Value != status)
        {
            contribution.Status = status.Value;
            changed = true;
        }

        if (!string.IsNullOrEmpty(mediaType) && contribution.MediaType != mediaType)
        {
            contribution.MediaType = mediaType;
            changed = true;
        }

        if (!string.IsNullOrEmpty(asin) && contribution.Asin != asin)
        {
            contribution.Asin = asin;
            changed = true;
        }

        if (!string.IsNullOrEmpty(upc) && contribution.Upc != upc)
        {
            contribution.Upc = upc;
            changed = true;
        }

        //contribution.FrontImageUrl = frontImageUrl;
        //contribution.BackImageUrl = backImageUrl;

        if (!string.IsNullOrEmpty(releaseSlug) && contribution.ReleaseSlug != releaseSlug)
        {
            contribution.ReleaseSlug = releaseSlug;
            changed = true;
        }

        if (!string.IsNullOrEmpty(locale) && contribution.Locale != locale)
        {
            contribution.Locale = locale;
            changed = true;
        }

        if (!string.IsNullOrEmpty(regionCode) && contribution.RegionCode != regionCode)
        {
            contribution.RegionCode = regionCode;
            changed = true;
        }

        //contribution.Title = title;
        //contribution.Year = year;
        //contribution.TitleSlug = titleSlug;

        if (changed)
        {
            await database.SaveChangesAsync(cancellationToken);
        }

        return contribution;
    }
}
