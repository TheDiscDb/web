using FluentResults;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Contribution;

public class ReleaseImageValidation : IContributionValidation
{
    private readonly IStaticAssetStore imageStore;

    public ReleaseImageValidation([FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore)
    {
        this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
    }

    public string DisplayName => "Release Has Images";

    public async Task<Result> Validate(UserContribution contribution, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(contribution.FrontImageUrl))
        {
            return Result.Fail("The release must have at least a front image.");
        }

        if (contribution.FrontImageUrl.StartsWith("/images/Contributions/", StringComparison.OrdinalIgnoreCase))
        {
            string remotePath = contribution.FrontImageUrl.Substring("/images/".Length);
            if (!await this.imageStore.Exists(remotePath, cancellationToken))
            {
                return Result.Fail("The front image was not uploaded successfully");
            }
        }

        return Result.Ok();
    }
}
