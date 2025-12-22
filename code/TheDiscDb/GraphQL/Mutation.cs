using Azure.Core;
using FluentResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL;

public class ContributionNotFoundException : Exception
{
    public ContributionNotFoundException(string encodedId) : base($"Contribution with id {encodedId} not found")
    {
    }
}

public class Mutation
{
    private readonly IdEncoder idEncoder;
    private readonly IPrincipalProvider principal;
    private readonly UserManager<TheDiscDbUser> userManager;
    private readonly IUserContributionService userContributionService;
    private readonly IStaticAssetStore assetStore;
    private readonly IStaticAssetStore imageStore;

    public Mutation(IdEncoder idEncoder, IPrincipalProvider principal, UserManager<TheDiscDbUser> userManager, IUserContributionService userContributionService, IStaticAssetStore assetStore, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore)
    {
        this.idEncoder = idEncoder ?? throw new ArgumentNullException(nameof(idEncoder));
        this.principal = principal ?? throw new ArgumentNullException(nameof(principal));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        this.userContributionService = userContributionService ?? throw new ArgumentNullException(nameof(userContributionService));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
    }

    public async Task<UserContribution> AddContribution(ContributionMutationRequest input, SqlServerDataContext database, CancellationToken cancellationToken)
    {
        var user = principal.Principal ?? throw new InvalidOperationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        var contribution = new UserContribution
        {
            UserId = userId,
            Created = DateTimeOffset.UtcNow,
            Asin = input.Asin,
            ExternalId = input.ExternalId,
            ExternalProvider = input.ExternalProvider,
            MediaType = input.MediaType,
            ReleaseDate = input.ReleaseDate,
            Status = UserContributionStatus.Pending,
            FrontImageUrl = input.FrontImageUrl,
            BackImageUrl = input.BackImageUrl,
            Upc = input.Upc,
            ReleaseTitle = input.ReleaseTitle,
            ReleaseSlug = input.ReleaseSlug,
            Locale = input.Locale,
            RegionCode = input.RegionCode,
            Title = input.Title,
            Year = input.Year,
            TitleSlug = CreateSlug(input.Title, input.Year)
        };

        database.UserContributions.Add(contribution);
        await database.SaveChangesAsync(cancellationToken);

        // Now that we have a contributionId, we can get the external data which will save it in blob storage
        if (string.IsNullOrEmpty(contribution.Title) || string.IsNullOrEmpty(contribution.Year))
        {
            var externalData = await this.userContributionService.GetExternalData(contribution.EncodedId, cancellationToken);
            if (externalData.IsSuccess)
            {
                contribution.Title = externalData.Value.Title;
                contribution.Year = externalData.Value.Year.ToString();
                await database.SaveChangesAsync(cancellationToken);
            }
        }

        //Now move the uploaded assets from temp storage to the contribution folder
        await MoveImages(database, contribution, input.FrontImageUrl ?? string.Empty, "front", (c, url) => c.FrontImageUrl = url, cancellationToken);
        await MoveImages(database, contribution, input.BackImageUrl ?? string.Empty, "back", (c, url) => c.BackImageUrl = url, cancellationToken);
        
        idEncoder.EncodeInPlace(contribution);
        return contribution;

        static string CreateSlug(string name, string year)
        {
            if (!string.IsNullOrEmpty(year))
            {
                return string.Format("{0}-{1}", name.Slugify(), year);
            }

            return name.Slugify();
        }
    }

    private async Task MoveImages(SqlServerDataContext dbContext, UserContribution contribution, string currentImageUrl, string name, Action<UserContribution, string> updateUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(currentImageUrl) && currentImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            // This shouldn't happen but just in case, we don't want to try and move an external URL
            return;
        }

        string remotePath = $"Contributions/releaseImages/{currentImageUrl}";
        if (await imageStore.Exists(remotePath, cancellationToken))
        {
            var data = await imageStore.Download(remotePath, cancellationToken);
            // Move the image to a folder based on the contribution id
            if (data != null)
            {
                var memoryStream = new MemoryStream(data.ToArray());

                string imageStoreRemotePath = $"Contributions/{contribution.EncodedId}/{name}.jpg";
                await imageStore.Save(memoryStream, imageStoreRemotePath, ContentTypes.ImageContentType, cancellationToken);

                memoryStream.Position = 0;
                string assetStoreRemotePath = $"{contribution.EncodedId}/{name}.jpg";
                await this.assetStore.Save(memoryStream, assetStoreRemotePath, ContentTypes.ImageContentType, cancellationToken);

                updateUrl(contribution, $"/images/Contributions/{contribution.EncodedId}/{name}.jpg");
                await dbContext.SaveChangesAsync(cancellationToken);

                // Delete from the old old location
                await imageStore.Delete(remotePath, cancellationToken);

                memoryStream.Dispose();
            }
        }
    }

    [Error(typeof(ContributionNotFoundException))]
    public async Task<HashDiscResponse> HashDisc(string contributionId, HashDiscRequest input, SqlServerDataContext database, CancellationToken cancellationToken = default)
    {
        int id = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.HashItems)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contribution == null)
        {
            throw new ContributionNotFoundException(contributionId);
        }

        var hash = input.Files.OrderBy(f => f.Name).CalculateHash();
        var existingItems = contribution.HashItems?.Where(i => i.DiscHash == hash).ToList();
        foreach (var existing in existingItems ?? Enumerable.Empty<UserContributionDiscHashItem>())
        {
            contribution.HashItems!.Remove(existing);
            database.UserContributionDiscHashItems.Remove(existing);
        }

        foreach (var item in input.Files)
        {
            contribution.HashItems!.Add(new UserContributionDiscHashItem
            {
                DiscHash = hash,
                CreationTime = item.CreationTime,
                Index = item.Index,
                Name = item.Name,
                Size = item.Size
            });
        }

        await database.SaveChangesAsync(cancellationToken);

        var response = new HashDiscResponse
        {
            DiscHash = hash
        };

        return response;
    }
}