namespace TheDiscDb.Client.Pages.Contribute;

public sealed record ReleaseDetailDraftState
{
    public string MediaType { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public Guid StorageId { get; init; }
    public string Asin { get; init; } = string.Empty;
    public bool AsinNotAvailable { get; init; }
    public string Upc { get; init; } = string.Empty;
    public string FrontImageUrl { get; init; } = string.Empty;
    public string? BackImageUrl { get; init; }
    public string ReleaseTitle { get; init; } = string.Empty;
    public string ReleaseSlug { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
    public string RegionCode { get; init; } = string.Empty;
    public DateTimeOffset ReleaseDate { get; init; }
    public string ReleaseDateText { get; init; } = string.Empty;
    public string? ContributionId { get; init; }
}
