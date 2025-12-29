using TheDiscDb.Data.Import;
using TheDiscDb.GraphQL.Contribute.Models;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    public async Task<UserContribution> CreateContribution(ContributionMutationRequest input, SqlServerDataContext database, CancellationToken cancellationToken)
    {
        var user = principal.Principal ?? throw new InvalidOperationException("No user principal available.");
        var userId = userManager.GetUserId(user);

        var contribution = new UserContribution
        {
            UserId = userId ?? "",
            Created = DateTimeOffset.UtcNow,
            Asin = input.Asin,
            ExternalId = input.ExternalId,
            ExternalProvider = input.ExternalProvider,
            MediaType = input.MediaType,
            ReleaseDate = input.ReleaseDate,
            Status = UserContributionStatus.Pending,
            FrontImageUrl = input.FrontImageUrl ?? "",
            BackImageUrl = input.BackImageUrl ?? "",
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
}