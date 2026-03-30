using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.GraphQL.Contribute.Models;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(AuthenticationException))]
    [Error(typeof(BoxsetNotFoundException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserContributionBoxset> UpdateBoxset(
        string boxsetId,
        BoxsetMutationRequest input,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken)
    {
        var boxset = await LoadAndVerifyBoxset(database, userManager, boxsetId, cancellationToken);

        boxset.Title = input.Title;
        boxset.SortTitle = input.SortTitle;
        boxset.Slug = input.Slug;
        boxset.FrontImageUrl = input.FrontImageUrl;
        boxset.BackImageUrl = input.BackImageUrl;
        boxset.Asin = input.Asin;
        boxset.Upc = input.Upc;
        boxset.ReleaseDate = input.ReleaseDate;
        boxset.Locale = input.Locale;
        boxset.RegionCode = input.RegionCode;

        await database.SaveChangesAsync(cancellationToken);

        boxset.EncodedId = this.idEncoder.Encode(boxset.Id);
        return boxset;
    }
}
