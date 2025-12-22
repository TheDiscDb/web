using System.ComponentModel.DataAnnotations;
using FluentResults;
using MakeMkv;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IUserContributionService
{
    Task<Result<List<UserContribution>>> GetUserContributions(CancellationToken cancellationToken = default);
    Task<Result<CreateContributionResponse>> CreateContribution(string userId, ContributionMutationRequest request, CancellationToken cancellationToken = default);
    Task<Result<UserContribution>> GetContribution(string contributionId, CancellationToken cancellationToken = default);
    Task<Result> DeleteContribution(string contributionId, CancellationToken cancellationToken = default);
    Task<Result> UpdateContribution(string contributionId, ContributionMutationRequest request, CancellationToken cancellationToken = default);
    Task<Result<HashDiscResponse>> HashDisc(string contributionId, HashDiscRequest request, CancellationToken cancellationToken = default);
    Task<Result<SeriesEpisodeNames>> GetEpisodeNames(string contributionId, CancellationToken cancellationToken = default);
    Task<Result<ExternalMetadata>> GetExternalData(string contributionId, CancellationToken cancellationToken = default);
    Task<Result<ExternalMetadata>> GetExternalData(string externalId, string mediaType, string provider, CancellationToken cancellationToken = default);
    Task<Result<ImportReleaseDetailsResponse>> ImportReleaseDetails(string asin, CancellationToken cancellationToken = default);

    Task<Result<List<UserContributionDisc>>> GetDiscs(string contributionId, CancellationToken cancellationToken = default);
    Task<Result<UserContributionDisc>> GetDisc(string contributionId, string discId, CancellationToken cancellationToken = default);
    Task<Result> SaveDiscLogs(string contributionId, string discId, string logs, CancellationToken cancellationToken = default);
    Task<Result<DiscLogResponse>> GetDiscLogs(string contributionId, string discId, CancellationToken cancellationToken = default);
    Task<Result<SaveDiscResponse>> CreateDisc(string contributionId, SaveDiscRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateDisc(string contributionId, string discId, SaveDiscRequest request, CancellationToken cancellationToken  = default);
    Task<Result> DeleteDisc(string contributionId, string discId, CancellationToken cancellationToken = default);
    Task<Result<DiscStatusResponse>> CheckDiskUploadStatus(string discId, CancellationToken cancellationToken = default);

    Task<Result<AddItemResponse>> AddItemToDisc(string contributionId, string discId, AddItemRequest request, CancellationToken cancellationToken = default);
    Task<Result> EditItemOnDisc(string contributionId, string discId, string itemId, EditItemRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteItemFromDisc(string contributionId, string discId, string itemId, CancellationToken cancellationToken = default);

    Task<Result<AddChapterResponse>> AddChapterToItem(string contributionId, string discId, string itemId, AddChapterRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteChapterFromItem(string contributionId, string discId, string itemId, string chapterId, CancellationToken cancellationToken = default);
    Task<Result> UpdateChapterInItem(string contributionId, string discId, string itemId, string chapterId, AddChapterRequest request, CancellationToken cancellationToken = default);

    Task<Result<AddAudioTrackResponse>> AddAudioTrackToItem(string contributionId, string discId, string itemId, AddAudioTrackRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAudioTrackFromItem(string contributionId, string discId, string itemId, string audioTrackId, CancellationToken cancellationToken = default);
    Task<Result> UpdateAudioTrackInItem(string contributionId, string discId, string itemId, string audioTrackId, AddAudioTrackRequest request, CancellationToken cancellationToken = default);
}

public class ContributionMutationRequest
{
    [Required]
    public string MediaType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalProvider { get; set; } = string.Empty;
    [Required]
    public DateTimeOffset ReleaseDate { get; set; }
    [Required]
    [RegularExpression(@"\w{10}", ErrorMessage = "ASIN must be a combination 10 characters or numbers")]
    public string Asin { get; set; } = string.Empty;
    [Required]
    [RegularExpression(@"\d{12}", ErrorMessage = "UPC must be exactly 12 digits")]
    public string Upc { get; set; } = string.Empty;

    [Required(ErrorMessage = "Front Image is required")]
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    [Required]
    public string ReleaseSlug { get; set; } = string.Empty;
    [Required]
    public string RegionCode { get; set; } = string.Empty;
    [Required]
    public string Locale { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public Guid StorageId { get; set; } = Guid.Empty;
    public UserContributionStatus Status { get; set; } = UserContributionStatus.Pending;

    public ContributionMutationRequest() { }

    public ContributionMutationRequest(UserContribution contribution)
    {
        this.MediaType = contribution.MediaType;
        this.ExternalId = contribution.ExternalId ?? string.Empty;
        this.ExternalProvider = contribution.ExternalProvider ?? string.Empty;
        this.ReleaseDate = contribution.ReleaseDate;
        this.Asin = contribution.Asin;
        this.Upc = contribution.Upc;
        this.FrontImageUrl = contribution.FrontImageUrl;
        this.BackImageUrl = contribution.BackImageUrl ?? string.Empty;
        this.ReleaseTitle = contribution.ReleaseTitle ?? string.Empty;
        this.ReleaseSlug = contribution.ReleaseSlug;
        this.RegionCode = contribution.RegionCode;
        this.Locale = contribution.Locale;
        this.Title = contribution.Title ?? string.Empty;
        this.Year = contribution.Year ?? string.Empty;
        this.Status = contribution.Status;
    }
}

public class CreateContributionResponse
{
    public string ContributionId { get; set; } = string.Empty;
}

public class SaveDiscRequest
{
    [Required]
    public string ContentHash { get; set; } = string.Empty;
    [Required]
    public string Format { get; set; } = string.Empty;
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string Slug { get; set; } = string.Empty;
    public string? ExistingDiscPath { get; set; }
}

public class SaveDiscResponse
{
    public string DiscId { get; set; } = string.Empty;
}

public class DiscStatusResponse
{
    public bool LogsUploaded { get; set; }
}

public class DiscLogResponse
{
    public DiscInfo? Info { get; set; }
    public UserContributionDisc? Disc { get; set; }
    public UserContribution? Contribution { get; set; }
}

public class AddItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int ChapterCount { get; set; } = 0;
    public int SegmentCount { get; set; } = 0;
    public string SegmentMap { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    public string? Season { get; set; } = string.Empty;
    public string? Episode { get; set; } = string.Empty;
}

public class AddItemResponse
{
    public string ItemId { get; set; } = string.Empty;
}

public class EditItemRequest : AddItemRequest
{
}

public class AddChapterRequest
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class AddChapterResponse
{
    public string ChapterId { get; set; } = string.Empty;
}

public class AddAudioTrackRequest
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class AddAudioTrackResponse
{
    public string AudioTrackId { get; set; } = string.Empty;
}

public class HashDiscRequest
{
    public List<FileHashInfo> Files { get; set; } = new List<FileHashInfo>();
}

public class HashDiscResponse
{
    public string DiscHash { get; set; } = string.Empty;
}

public class ImportReleaseDetailsResponse
{
    public string? Title { get; set; }
    public string? RegionCode { get; set; }
    public string? Locale { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public string? Upc { get; set; }
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    public string? MediaFormat { get; set; }
}