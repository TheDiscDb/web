using TheDiscDb.Services;

namespace TheDiscDb.Client.Services;

public class AmazonImporter : IAmazonImporter
{
    public Task<AmazonProductMetadata?> GetProductMetadataAsync(string asin, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
