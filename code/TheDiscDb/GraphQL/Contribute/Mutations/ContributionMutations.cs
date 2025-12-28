using Microsoft.AspNetCore.Identity;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    private readonly IdEncoder idEncoder;
    private readonly IPrincipalProvider principal;
    private readonly UserManager<TheDiscDbUser> userManager;
    private readonly IUserContributionService userContributionService;
    private readonly IStaticAssetStore assetStore;
    private readonly IStaticAssetStore imageStore;

    public ContributionMutations(IdEncoder idEncoder, IPrincipalProvider principal, UserManager<TheDiscDbUser> userManager, IUserContributionService userContributionService, IStaticAssetStore assetStore, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore)
    {
        this.idEncoder = idEncoder ?? throw new ArgumentNullException(nameof(idEncoder));
        this.principal = principal ?? throw new ArgumentNullException(nameof(principal));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        this.userContributionService = userContributionService ?? throw new ArgumentNullException(nameof(userContributionService));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
    }
}
