using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    private readonly IdEncoder idEncoder;
    private readonly IPrincipalProvider principal;
    private readonly UserManager<TheDiscDbUser> userManager;
    private readonly IStaticAssetStore assetStore;
    private readonly IStaticAssetStore imageStore;

    public ContributionMutations(IdEncoder idEncoder, IPrincipalProvider principal, UserManager<TheDiscDbUser> userManager, IStaticAssetStore assetStore, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore)
    {
        this.idEncoder = idEncoder ?? throw new ArgumentNullException(nameof(idEncoder));
        this.principal = principal ?? throw new ArgumentNullException(nameof(principal));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
    }

    private async Task EnsureOwnership(UserContribution? contribution, string contributionId, string? discId = null, string? itemId = null, CancellationToken cancellationToken = default)
    {
        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        var decodedContributionId = this.idEncoder.Decode(contributionId);
        if (decodedContributionId == 0)
        {
            throw new InvalidIdException(contributionId, "Contribution");
        }

        if (contribution == null)
        {
            throw new ContributionNotFoundException(contributionId);
        }

        if (contribution.UserId != userId)
        {
            throw new InvalidOwnershipException(contributionId, "Contribution");
        }

        if (!string.IsNullOrEmpty(discId) && contribution.Discs.Any())
        {
            int decodedDiscId = this.idEncoder.Decode(discId);
            if (decodedDiscId == 0)
            {
                throw new InvalidIdException(discId!, "Disc");
            }

            if (contribution.Discs.Any(d => d.Id == decodedDiscId) == false)
            {
                throw new InvalidOwnershipException(discId!, "Disc");
            }
        }

        if (!string.IsNullOrEmpty(itemId) && contribution.Discs.Any())
        {
            var decodedItemId = this.idEncoder.Decode(itemId);
            if (decodedItemId == 0)
            {
                throw new InvalidIdException(itemId!, "DiscItem");
            }

            var itemFound = contribution.Discs
                .SelectMany(d => d.Items)
                .Any(i => i.Id == decodedItemId);

            if (!itemFound)
            {
                throw new InvalidOwnershipException(itemId!, "DiscItem");
            }
        }
    }
}
