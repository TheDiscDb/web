using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services;
using TheDiscDb.Services.Contributions;
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
    /// <summary>
    /// True when this thread belongs to a UserContributionBoxset (and ContributionId actually
    /// holds the boxset id). False for regular contribution threads.
    /// </summary>
    public bool IsBoxset { get; set; }
}

public class ContributionQuery(IdEncoder idEncoder)
{
    const int MaxPageSize = 100;
    const int DefaultPageSize = 50;

    [Authorize]
    public string? GetCurrentUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

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
            return context.UserMessages.Where(m => false);
        }

        var decodedId = idEncoder.Decode(contributionId);

        return MessageAccess.VisibleTo(context.UserMessages, user)
            .Where(m => m.ContributionId == decodedId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id);
    }

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseSorting]
    [Authorize]
    public IQueryable<UserMessage> GetBoxsetChat(SqlServerDataContext context, string boxsetId, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return context.UserMessages.Where(m => false);
        }

        var decodedId = idEncoder.Decode(boxsetId);

        return MessageAccess.VisibleTo(context.UserMessages, user)
            .Where(m => m.BoxsetId == decodedId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id);
    }

    [Authorize]
    public async Task<bool> HasUnreadMessages(SqlServerDataContext context, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return false;

        return await MessageAccess.Inbox(context.UserMessages, user)
            .AnyAsync(e => e.IsUnread);
    }

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseSorting]
    [Authorize]
    public IQueryable<UserMessage> GetMyMessages(SqlServerDataContext context, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return context.UserMessages.Where(m => false);
        }

        return MessageAccess.VisibleTo(context.UserMessages, user)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id);
    }

    [Authorize]
    public async Task<List<MessageThread>> GetMessageThreads(SqlServerDataContext context, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return [];

        // Aggregate contribution-scoped threads in SQL.
        var inbox = MessageAccess.Inbox(context.UserMessages, user);
        var contributionThreads = await inbox
            .Where(e => e.Message.ContributionId != null)
            .Select(e => new
            {
                ContributionId = e.Message.ContributionId!.Value,
                e.Message.Id,
                e.IsUnread,
                e.Message.CreatedAt,
                e.Message.Message
            })
            .GroupBy(m => m.ContributionId)
            .Select(g => new
            {
                ContributionId = g.Key,
                LastMessageAt = g.Max(m => m.CreatedAt),
                UnreadCount = g.Count(m => m.IsUnread),
                TotalCount = g.Count(),
                LastMessageText = g.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Message).First()
            })
            .ToListAsync();

        // Aggregate boxset-scoped threads in SQL.
        var boxsetThreads = await inbox
            .Where(e => e.Message.BoxsetId != null)
            .Select(e => new
            {
                BoxsetId = e.Message.BoxsetId!.Value,
                e.Message.Id,
                e.IsUnread,
                e.Message.CreatedAt,
                e.Message.Message
            })
            .GroupBy(m => m.BoxsetId)
            .Select(g => new
            {
                BoxsetId = g.Key,
                LastMessageAt = g.Max(m => m.CreatedAt),
                UnreadCount = g.Count(m => m.IsUnread),
                TotalCount = g.Count(),
                LastMessageText = g.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Message).First()
            })
            .ToListAsync();

        if (contributionThreads.Count == 0 && boxsetThreads.Count == 0)
            return [];

        // Resolve display titles for both types.
        var contributionIds = contributionThreads.Select(t => t.ContributionId).ToList();
        var contributions = await context.UserContributions
            .Where(c => contributionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ReleaseTitle, c.Title })
            .ToDictionaryAsync(c => c.Id, c => new { c.ReleaseTitle, c.Title });

        var boxsetIds = boxsetThreads.Select(t => t.BoxsetId).ToList();
        var boxsets = await context.UserContributionBoxsets
            .Where(b => boxsetIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Title })
            .ToDictionaryAsync(b => b.Id, b => b.Title);

        static string Trim(string text) => text.Length > 100 ? text[..100] + "…" : text;

        var threads = new List<MessageThread>(contributionThreads.Count + boxsetThreads.Count);

        foreach (var t in contributionThreads)
        {
            var contrib = contributions.GetValueOrDefault(t.ContributionId);
            threads.Add(new MessageThread
            {
                ContributionId = t.ContributionId,
                EncodedContributionId = idEncoder.Encode(t.ContributionId),
                ContributionTitle = contrib?.ReleaseTitle ?? "Deleted Contribution",
                MediaTitle = contrib?.Title,
                LastMessagePreview = Trim(t.LastMessageText),
                LastMessageAt = t.LastMessageAt,
                UnreadCount = t.UnreadCount,
                TotalCount = t.TotalCount,
                IsBoxset = false,
            });
        }

        foreach (var t in boxsetThreads)
        {
            var title = boxsets.GetValueOrDefault(t.BoxsetId) ?? "Deleted Boxset";
            threads.Add(new MessageThread
            {
                ContributionId = t.BoxsetId,
                EncodedContributionId = idEncoder.Encode(t.BoxsetId),
                ContributionTitle = title,
                MediaTitle = "Boxset",
                LastMessagePreview = Trim(t.LastMessageText),
                LastMessageAt = t.LastMessageAt,
                UnreadCount = t.UnreadCount,
                TotalCount = t.TotalCount,
                IsBoxset = true,
            });
        }

        return threads.OrderByDescending(t => t.LastMessageAt).ToList();
    }

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    [Authorize("Admin")]
    public IQueryable<UserContributionBoxset> GetBoxsetContributions(SqlServerDataContext context) => context.UserContributionBoxsets;

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    [Authorize]
    public IQueryable<UserContributionBoxset> GetMyBoxsets(SqlServerDataContext context, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Enumerable.Empty<UserContributionBoxset>().AsQueryable();
        }

        return context.UserContributionBoxsets.Where(b => b.UserId == userId);
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

    [Authorize]
    public Task<IntakeReleaseMatch?> GetIntakeReleaseMatch(
        string externalProvider,
        string externalId,
        string upc,
        IIntakeMatchingService intakeMatchingService,
        CancellationToken cancellationToken) =>
        intakeMatchingService.FindReleaseMatchAsync(
            externalProvider,
            externalId,
            upc,
            cancellationToken);

    [Authorize]
    public async Task<IntakeDiscMatch?> GetIntakeDiscMatch(
        string contributionId,
        string contentHash,
        string? format,
        string? globalDiscId,
        ClaimsPrincipal user,
        IIntakeMatchingService intakeMatchingService,
        CancellationToken cancellationToken)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        int decodedContributionId;
        try
        {
            decodedContributionId = idEncoder.Decode(contributionId);
        }
        catch
        {
            return null;
        }

        return await intakeMatchingService.FindDiscMatchAsync(
            decodedContributionId,
            userId,
            contentHash,
            format,
            globalDiscId,
            cancellationToken);
    }
}