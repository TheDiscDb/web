using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    private static readonly UserContributionStatus[] EditableStatuses =
    [
        UserContributionStatus.Pending,
        UserContributionStatus.ChangesRequested,
        UserContributionStatus.Rejected
    ];

    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Error(typeof(InvalidContributionStatusException))]
    [Authorize]
    public async Task<UserContribution> UpdateContribution(
        [Service] SqlServerDataContext database,
        string contributionId,
        string asin,
        string upc,
        DateTimeOffset releaseDate,
        string releaseTitle,
        string releaseSlug,
        string locale,
        string regionCode,
        string? frontImageUrl,
        string? backImageUrl,
        bool deleteBackImage,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(userManager, contribution, contributionId, cancellationToken: cancellationToken);

        if (!EditableStatuses.Contains(contribution!.Status))
        {
            throw new InvalidContributionStatusException(contribution.Status.ToString());
        }

        contribution.Asin = asin;
        contribution.Upc = upc;
        contribution.ReleaseDate = releaseDate;
        contribution.ReleaseTitle = releaseTitle;
        contribution.ReleaseSlug = releaseSlug;
        contribution.Locale = locale;
        contribution.RegionCode = regionCode;

        if (!string.IsNullOrEmpty(frontImageUrl))
        {
            contribution.FrontImageUrl = frontImageUrl;
        }

        if (deleteBackImage)
        {
            string encodedId = this.idEncoder.Encode(contribution.Id);
            await this.imageStore.Delete($"Contributions/{encodedId}/back.jpg", cancellationToken);
            await this.assetStore.Delete($"{encodedId}/back.jpg", cancellationToken);
            contribution.BackImageUrl = null;
        }
        else if (!string.IsNullOrEmpty(backImageUrl))
        {
            contribution.BackImageUrl = backImageUrl;
        }

        await database.SaveChangesAsync(cancellationToken);
        this.idEncoder.EncodeInPlace(contribution);
        return contribution;
    }
}
