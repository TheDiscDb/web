namespace TheDiscDb.Services;

public interface IAmazonImporter
{
    Task<AmazonProductMetadata?> GetProductMetadataAsync(string asin, CancellationToken cancellationToken = default);
}

public class AmazonImporter : IAmazonImporter
{
    public Task<AmazonProductMetadata?> GetProductMetadataAsync(string asin, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}

public class AmazonProductMetadata
{
    public string? Title { get; set; }
    public string? Upc { get; set; }
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public int? NumberOfDiscs { get; set; }
}
