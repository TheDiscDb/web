using Fantastic.TheMovieDb;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
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
    private readonly TheMovieDbClient tmdb;

    public ContributionMutations(IdEncoder idEncoder, IPrincipalProvider principal, UserManager<TheDiscDbUser> userManager, IStaticAssetStore assetStore, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore, TheMovieDbClient tmdb)
    {
        this.idEncoder = idEncoder ?? throw new ArgumentNullException(nameof(idEncoder));
        this.principal = principal ?? throw new ArgumentNullException(nameof(principal));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
        this.tmdb = tmdb ?? throw new ArgumentNullException(nameof(tmdb));
    }
}
