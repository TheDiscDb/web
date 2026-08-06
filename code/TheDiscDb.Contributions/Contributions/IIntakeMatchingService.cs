namespace TheDiscDb.Services.Contributions;

using TheDiscDb.Web.Data;

public sealed record IntakeReleaseMatch(
    string? Asin,
    string Upc,
    DateTimeOffset? ReleaseDate,
    string? ReleaseTitle,
    string? ReleaseSlug,
    string? Locale,
    string? RegionCode,
    string? FrontImageUrl);

public sealed record IntakeDiscMatch(
    string ContentHash,
    string Format,
    string? Name,
    string? Slug,
    string? EvidenceGlobalDiscId,
    bool GlobalDiscIdMismatch,
    bool HasScanLog);

public sealed record IntakeDiscPromotionRequest(
    string ContentHash,
    string Format,
    string Name,
    string Slug,
    string? GlobalDiscId);

public sealed record IntakeDiscPromotionResult(
    UserContributionDisc Disc,
    bool Promoted,
    bool LogsCopied,
    string? EvidenceGlobalDiscId,
    bool GlobalDiscIdMismatch,
    bool MainDatabaseMatch);

public interface IIntakeMatchingService
{
    Task<IntakeReleaseMatch?> FindReleaseMatchAsync(
        string externalProvider,
        string externalId,
        string upc,
        CancellationToken cancellationToken = default);

    Task<IntakeDiscMatch?> FindDiscMatchAsync(
        int contributionId,
        string userId,
        string contentHash,
        string? format,
        string? globalDiscId,
        CancellationToken cancellationToken = default);

    Task<IntakeDiscPromotionResult?> PromoteDiscAsync(
        int contributionId,
        string userId,
        IntakeDiscPromotionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> RecordReleasePromotionAsync(
        int contributionId,
        string userId,
        CancellationToken cancellationToken = default);
}
