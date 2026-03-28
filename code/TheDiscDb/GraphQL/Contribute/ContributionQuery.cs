using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;
using HotChocolate.Authorization;

namespace TheDiscDb.GraphQL.Contribute;

public class MessageThread
{
    public int ContributionId { get; set; }
    public string EncodedContributionId { get; set; } = string.Empty;
    public string ContributionTitle { get; set; } = string.Empty;
    public string? MediaTitle { get; set; }
    public string LastMessagePreview { get; set; } = string.Empty;
    public DateTimeOffset LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
}

public class ContributionQuery(IdEncoder idEncoder)
{
    const int MaxPageSize = 100;
    const int DefaultPageSize = 50;

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    [Authorize("Admin")]
    public IQueryable<UserContribution> GetContributions(SqlServerDataContext context) => context.UserContributions;

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    [Authorize]
    public IQueryable<UserContribution> GetMyContributions(SqlServerDataContext context, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Enumerable.Empty<UserContribution>().AsQueryable();
        }

        return context.UserContributions.Where(c => c.UserId == userId);
    }

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseSorting]
    [Authorize("Admin")]
    public IQueryable<ContributionHistory> GetContributionHistory(SqlServerDataContext context, int contributionId) =>
        context.ContributionHistory
            .Where(h => h.ContributionId == contributionId)
            .OrderByDescending(h => h.TimeStamp);

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseSorting]
    [Authorize]
    public IQueryable<UserMessage> GetContributionChat(SqlServerDataContext context, string contributionId, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Enumerable.Empty<UserMessage>().AsQueryable();
        }

        var decodedId = idEncoder.Decode(contributionId);

        // Verify the user owns this contribution
        var ownsContribution = context.UserContributions.Any(c => c.Id == decodedId && c.UserId == userId);
        if (!ownsContribution)
        {
            return Enumerable.Empty<UserMessage>().AsQueryable();
        }

        return context.UserMessages
            .Where(m => m.ContributionId == decodedId)
            .OrderByDescending(m => m.CreatedAt);
    }

    [Authorize]
    public async Task<bool> HasUnreadMessages(SqlServerDataContext context, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return false;

        return await context.UserMessages
            .AnyAsync(m => m.ToUserId == userId && !m.IsRead);
    }

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseSorting]
    [Authorize]
    public IQueryable<UserMessage> GetMyMessages(SqlServerDataContext context, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Enumerable.Empty<UserMessage>().AsQueryable();
        }

        return context.UserMessages
            .Where(m => m.ToUserId == userId || m.FromUserId == userId)
            .OrderByDescending(m => m.CreatedAt);
    }

    [Authorize]
    public async Task<List<MessageThread>> GetMessageThreads(SqlServerDataContext context, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return [];

        // Aggregate in SQL — avoids loading full message bodies into memory
        var threadData = await context.UserMessages
            .Where(m => m.ToUserId == userId || m.FromUserId == userId)
            .GroupBy(m => m.ContributionId)
            .Select(g => new
            {
                ContributionId = g.Key,
                LastMessageAt = g.Max(m => m.CreatedAt),
                UnreadCount = g.Count(m => m.ToUserId == userId && !m.IsRead),
                TotalCount = g.Count(),
                LastMessageText = g.OrderByDescending(m => m.CreatedAt).Select(m => m.Message).First()
            })
            .OrderByDescending(t => t.LastMessageAt)
            .ToListAsync();

        if (threadData.Count == 0)
            return [];

        var contributionIds = threadData.Select(t => t.ContributionId).ToList();
        var contributions = await context.UserContributions
            .Where(c => contributionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ReleaseTitle, c.Title })
            .ToDictionaryAsync(c => c.Id, c => new { c.ReleaseTitle, c.Title });

        return threadData
            .Select(t =>
            {
                var preview = t.LastMessageText.Length > 100
                    ? t.LastMessageText[..100] + "…"
                    : t.LastMessageText;

                var contrib = contributions.GetValueOrDefault(t.ContributionId);
                return new MessageThread
                {
                    ContributionId = t.ContributionId,
                    EncodedContributionId = idEncoder.Encode(t.ContributionId),
                    ContributionTitle = contrib?.ReleaseTitle ?? "Deleted Contribution",
                    MediaTitle = contrib?.Title,
                    LastMessagePreview = preview,
                    LastMessageAt = t.LastMessageAt,
                    UnreadCount = t.UnreadCount,
                    TotalCount = t.TotalCount
                };
            })
            .ToList();
    }

    [Authorize]
    public async Task<AmazonProductMetadata?> GetAmazonProductMetadata(string asin, IAmazonImporter importer, ILogger<ContributionQuery> logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(asin) || !System.Text.RegularExpressions.Regex.IsMatch(asin, @"^\w{10}$"))
        {
            return null;
        }

        try
        {
            return await importer.GetProductMetadataAsync(asin, cancellationToken);
        }
        catch (AmazonImportException ex)
        {
            logger.LogWarning(ex, "Amazon import failed for ASIN {Asin}", asin);
            return null;
        }
    }
}
