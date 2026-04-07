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
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(userManager, contribution, contributionId, cancellationToken: cancellationToken);

        contribution!.Asin = asin;
        contribution.Upc = upc;
        contribution.ReleaseDate = releaseDate;
        contribution.ReleaseTitle = releaseTitle;
        contribution.ReleaseSlug = releaseSlug;
        contribution.Locale = locale;
        contribution.RegionCode = regionCode;

        await database.SaveChangesAsync(cancellationToken);
        this.idEncoder.EncodeInPlace(contribution);
        return contribution;
    }
}
