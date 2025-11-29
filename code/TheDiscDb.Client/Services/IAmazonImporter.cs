namespace TheDiscDb.Services;

public interface IAmazonImporter
{
    Task<AmazonProductMetadata?> GetProductMetadataAsync(string asin, CancellationToken cancellationToken = default);
}

public class AmazonProductMetadata
{
    public string? Asin { get; set; }
    public string? Title { get; set; }
    public string? Upc { get; set; }
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public int? NumberOfDiscs { get; set; }

    public string? AspectRatio { get; set; }
    public bool? IsDiscontinued { get; set; }
    public string? MpaaRating { get; set; }
    public string? ModelNumber { get; set; }
    public string? Director { get; set; }
    public string? MediaFormat { get; set; }
    public string? Actors { get; set; }
    public string? Producers { get; set; }
    public string? Language { get; set; }
    public string? Dubbed { get; set; }
    public string? Subtitles { get; set; }
    public string? Studio { get; set; }
}