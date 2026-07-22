namespace TheDiscDb.Services.Contributions;

using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Web.Data;

/// <summary>
/// Metadata for creating a pending contribution around a single disc the contributor owns
/// but which isn't yet in the database. Only the disc identity fields are required; the
/// release/title metadata is optional and may be filled in later via the web contribute UI.
/// </summary>
public sealed record ContributionDiscRequest
{
    /// <summary>Content-hash fingerprint of the disc (as computed by <c>CalculateHash</c>).</summary>
    public required string ContentHash { get; init; }

    /// <summary>Globally-stable Disc ID (AACS SHA-1 / DVD MD5), if computed.</summary>
    public string? GlobalDiscId { get; init; }

    /// <summary>Disc format, e.g. "Blu-ray", "UHD", "DVD".</summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>Display name for the disc within the contribution.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Disc slug.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Raw MakeMKV log text for the disc. Validated and stored when non-empty.</summary>
    public string? DiscLogs { get; init; }

    // Optional pending-contribution metadata (safe to leave blank for an unknown disc).
    public string MediaType { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Year { get; init; }
    public string ReleaseTitle { get; init; } = string.Empty;
    public string? ReleaseSlug { get; init; }
    public string Locale { get; init; } = string.Empty;
    public string RegionCode { get; init; } = string.Empty;
    public string Upc { get; init; } = string.Empty;
    public string Asin { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public string ExternalProvider { get; init; } = "TMDB";
}

/// <summary>Result of creating a pending disc contribution.</summary>
public sealed record ContributionDiscResult(
    int ContributionId,
    int DiscId,
    string EncodedContributionId,
    string EncodedDiscId,
    bool LogsUploaded,
    string? LogUploadError,
    bool GlobalDiscIdCollision = false);

public interface IContributionDiscService
{
    /// <summary>
    /// Creates a <see cref="UserContributionStatus.Pending"/> <see cref="UserContribution"/> with a
    /// single <see cref="UserContributionDisc"/> and, when logs are supplied, validates and stores
    /// them in the contributions asset store at the canonical
    /// <c>{encodedContributionId}/{encodedDiscId}-logs.txt</c> path.
    /// </summary>
    Task<ContributionDiscResult> CreatePendingDiscContributionAsync(
        string userId,
        ContributionDiscRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a disc to an <em>existing</em> owned contribution (identified by its encoded id) and, when
    /// logs are supplied, validates and stores them at the canonical path. Idempotent on
    /// <see cref="ContributionDiscRequest.ContentHash"/>. Returns <c>null</c> when the contribution is
    /// not found or not owned by <paramref name="userId"/>.
    /// </summary>
    Task<ContributionDiscResult?> AddDiscToContributionAsync(
        string userId,
        string encodedContributionId,
        ContributionDiscRequest request,
        CancellationToken cancellationToken = default);
}
