using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Services.Contributions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Error(typeof(IntakeDiscMatchNotFoundException))]
    [Error(typeof(InvalidContributionStatusException))]
    [Authorize]
    public async Task<IntakeDiscPromotionResult> PromoteIntakeDisc(
        string contributionId,
        string contentHash,
        string format,
        string name,
        string slug,
        string? globalDiscId,
        [Service] SqlServerDataContext database,
        IIntakeMatchingService intakeMatchingService,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(
            userManager,
            contribution,
            contributionId,
            cancellationToken: cancellationToken);

        if (!contribution!.Status.IsEditableByOwner())
        {
            throw new InvalidContributionStatusException(contribution.Status.ToString());
        }

        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        var result = await intakeMatchingService.PromoteDiscAsync(
            contribution.Id,
            userId,
            new IntakeDiscPromotionRequest(contentHash, format, name, slug, globalDiscId),
            cancellationToken);
        if (result is null)
        {
            throw new IntakeDiscMatchNotFoundException();
        }

        if (result.Disc.Id > 0)
        {
            this.idEncoder.EncodeInPlace(result.Disc);
        }

        return result;
    }
}
