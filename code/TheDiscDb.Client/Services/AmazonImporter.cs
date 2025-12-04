using FluentResults;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Services;

public class AmazonImporter : IAmazonImporter
{
    Task<Result<AmazonProductMetadata?>> IAmazonImporter.GetProductMetadataAsync(string asin, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
