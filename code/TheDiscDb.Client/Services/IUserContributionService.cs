using FluentResults;
using MakeMkv;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IUserContributionService
{
    Task<Result<List<UserContribution>>> GetUserContributions(string userId, CancellationToken cancellationToken = default);
    Task<Result<CreateContributionResponse>> CreateContribution(string userId, CreateContributionRequest request, CancellationToken cancellationToken = default);
    Task<Result<UserContribution>> GetContribution(string contributionId, CancellationToken cancellationToken = default);
    Task<Result> DeleteContribution(string contributionId, CancellationToken cancellationToken = default);
    Task<Result> UpdateContribution(string contributionId, CreateContributionRequest request, CancellationToken cancellationToken = default);

    Task<Result<List<UserContributionDisc>>> GetDiscs(string contributionId, CancellationToken cancellationToken = default);
    Task<Result> SaveDiscLogs(string contributionId, string discId, string logs, CancellationToken cancellationToken = default);
    Task<Result<DiscLogResponse>> GetDiscLogs(string contributionId, string discId, CancellationToken cancellationToken = default);
    Task<Result<SaveDiscResponse>> CreateDisc(string contributionId, SaveDiscRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateDisc(string contributionId, string discId, SaveDiscRequest request, CancellationToken cancellationToken  = default);
    Task<Result> DeleteDisc(string contributionId, string discId, CancellationToken cancellationToken = default);
    Task<Result<DiscStatusResponse>> CheckDiskUploadStatus(string discId, CancellationToken cancellationToken = default);

    Task<Result<AddItemResponse>> AddItemToDisc(string contributionId, string discId, AddItemRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteItemFromDisc(string contributionId, string discId, string itemId, CancellationToken cancellationToken = default);

    Task<Result<AddChapterResponse>> AddChapterToItem(string contributionId, string discId, string itemId, AddChapterRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteChapterFromItem(string contributionId, string discId, string itemId, string chapterId, CancellationToken cancellationToken = default);
    Task<Result> UpdateChapterInItem(string contributionId, string discId, string itemId, string chapterId, AddChapterRequest request, CancellationToken cancellationToken = default);

    Task<Result<AddAudioTrackResponse>> AddAudioTrackToItem(string contributionId, string discId, string itemId, AddAudioTrackRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAudioTrackFromItem(string contributionId, string discId, string itemId, string audioTrackId, CancellationToken cancellationToken = default);
    Task<Result> UpdateAudioTrackInItem(string contributionId, string discId, string itemId, string audioTrackId, AddAudioTrackRequest request, CancellationToken cancellationToken = default);
}

public class CreateContributionRequest
{
    public string DiscHash { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalProvider { get; set; } = string.Empty;
    public DateTimeOffset ReleaseDate { get; set; }
    public string Asin { get; set; } = string.Empty;
    public string Upc { get; set; } = string.Empty;
    public string FrontImageUrl { get; set; } = string.Empty;
    public string BackImageUrl { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ReleaseSlug { get; set; } = string.Empty;
}

public class CreateContributionResponse
{
    public string ContributionId { get; set; } = string.Empty;
}

public class SaveDiscRequest
{
    public int Index { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
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
}

public class AddItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public int ChapterCount { get; set; } = 0;
    public int SegmentCount { get; set; } = 0;
    public string SegmentMap { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Year { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    public string? Season { get; set; } = string.Empty;
    public string? Episode { get; set; } = string.Empty;
}

public class AddItemResponse
{
    public string ItemId { get; set; } = string.Empty;
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