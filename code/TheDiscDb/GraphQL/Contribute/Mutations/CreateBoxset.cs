using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.GraphQL.Contribute.Models;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(AuthenticationException))]
    [Authorize]
    public async Task<UserContributionBoxset> CreateBoxset(
        BoxsetMutationRequest input,
        SqlServerDataContext database,
        IContributionHistoryService historyService,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(userManager);

        var boxset = new UserContributionBoxset
        {
            UserId = userId,
            Created = DateTimeOffset.UtcNow,
            Status = UserContributionStatus.Pending,
            Title = input.Title,
            SortTitle = input.SortTitle,
            Slug = input.Slug,
            FrontImageUrl = input.FrontImageUrl,
            BackImageUrl = input.BackImageUrl,
            Asin = input.Asin,
            Upc = input.Upc,
            ReleaseDate = input.ReleaseDate,
            Locale = input.Locale,
            RegionCode = input.RegionCode,
        };

        database.UserContributionBoxsets.Add(boxset);
        await database.SaveChangesAsync(cancellationToken);

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);

        return boxset;
    }
}
