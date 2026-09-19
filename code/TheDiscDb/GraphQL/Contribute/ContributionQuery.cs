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
    public MessageThreadKind Kind { get; set; }
    public int EntityId { get; set; }
    public string EncodedEntityId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Route { get; set; } = string.Empty;
    public string LastMessagePreview { get; set; } = string.Empty;
    public DateTimeOffset LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
}

public enum MessageThreadKind
{
    Contribution,
    Boxset,
    EditSuggestion,
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

        // Admins can view any contribution's messages; regular users must own the contribution.
        if (!user.IsInRole(DefaultRoles.Administrator))
        {
            var ownsContribution = context.UserContributions.Any(c => c.Id == decodedId && c.UserId == userId);
            if (!ownsContribution)
            {
                return Enumerable.Empty<UserMessage>().AsQueryable();
            }
        }

        return context.UserMessages
            .Where(m => m.ContributionId == decodedId)
            .OrderByDescending(m => m.CreatedAt);
    }

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize, IncludeTotalCount = true)]
    [UseSorting]
    [Authorize]
    public IQueryable<UserMessage> GetBoxsetChat(SqlServerDataContext context, string boxsetId, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Enumerable.Empty<UserMessage>().AsQueryable();
        }

        var decodedId = idEncoder.Decode(boxsetId);

        // Admins can view any boxset's messages; regular users must own the boxset.
        if (!user.IsInRole(DefaultRoles.Administrator))
        {
            var ownsBoxset = context.UserContributionBoxsets.Any(b => b.Id == decodedId && b.UserId == userId);
            if (!ownsBoxset)
            {
                return Enumerable.Empty<UserMessage>().AsQueryable();
            }
        }

        return context.UserMessages
            .Where(m => m.BoxsetId == decodedId)
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
        var isAdmin = user.IsInRole(DefaultRoles.Administrator);

        // Aggregate contribution-scoped threads in SQL.
        var contributionThreads = await context.UserMessages
            .Where(m => (m.ToUserId == userId || m.FromUserId == userId) && m.ContributionId != null)
            .Select(m => new
            {
                ContributionId = m.ContributionId!.Value,
                m.ToUserId,
                m.FromUserId,
                m.IsRead,
                m.CreatedAt,
                m.Message
            })
            .GroupBy(m => m.ContributionId)
            .Select(g => new
            {
                ContributionId = g.Key,
                LastMessageAt = g.Max(m => m.CreatedAt),
                UnreadCount = g.Count(m => m.ToUserId == userId && !m.IsRead),
                TotalCount = g.Count(),
                LastMessageText = g.OrderByDescending(m => m.CreatedAt).Select(m => m.Message).First()
            })
            .ToListAsync();

        // Aggregate boxset-scoped threads in SQL.
        var boxsetThreads = await context.UserMessages
            .Where(m => (m.ToUserId == userId || m.FromUserId == userId) && m.BoxsetId != null)
            .Select(m => new
            {
                BoxsetId = m.BoxsetId!.Value,
                m.ToUserId,
                m.FromUserId,
                m.IsRead,
                m.CreatedAt,
                m.Message
            })
            .GroupBy(m => m.BoxsetId)
            .Select(g => new
            {
                BoxsetId = g.Key,
                LastMessageAt = g.Max(m => m.CreatedAt),
                UnreadCount = g.Count(m => m.ToUserId == userId && !m.IsRead),
                TotalCount = g.Count(),
                LastMessageText = g.OrderByDescending(m => m.CreatedAt).Select(m => m.Message).First()
            })
            .ToListAsync();

        var suggestionThreads = await context.UserMessages
            .Where(m => (m.ToUserId == userId || m.FromUserId == userId) && m.EditSuggestionId != null)
            .Select(m => new
            {
                EditSuggestionId = m.EditSuggestionId!.Value,
                m.ToUserId,
                m.FromUserId,
                m.IsRead,
                m.CreatedAt,
                m.Message
            })
            .GroupBy(m => m.EditSuggestionId)
            .Select(g => new
            {
                EditSuggestionId = g.Key,
                LastMessageAt = g.Max(m => m.CreatedAt),
                UnreadCount = g.Count(m => m.ToUserId == userId && !m.IsRead),
                TotalCount = g.Count(),
                LastMessageText = g.OrderByDescending(m => m.CreatedAt).Select(m => m.Message).First()
            })
            .ToListAsync();

        if (contributionThreads.Count == 0 && boxsetThreads.Count == 0 && suggestionThreads.Count == 0)
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

        var suggestionIds = suggestionThreads.Select(t => t.EditSuggestionId).ToList();
        var suggestionQuery = context.EditSuggestions
            .Where(s => suggestionIds.Contains(s.Id));
        if (!isAdmin)
        {
            suggestionQuery = suggestionQuery.Where(s => s.UserId == userId);
        }

        var suggestions = await suggestionQuery
            .Select(s => new { s.Id, s.Summary, s.TargetEntityType, s.TargetEntityKey })
            .ToDictionaryAsync(s => s.Id);

        static string Trim(string text) => text.Length > 100 ? text[..100] + "…" : text;

        var threads = new List<MessageThread>(
            contributionThreads.Count + boxsetThreads.Count + suggestionThreads.Count);

        foreach (var t in contributionThreads)
        {
            var contrib = contributions.GetValueOrDefault(t.ContributionId);
            threads.Add(new MessageThread
            {
                Kind = MessageThreadKind.Contribution,
                EntityId = t.ContributionId,
                EncodedEntityId = idEncoder.Encode(t.ContributionId),
                Title = contrib?.ReleaseTitle ?? "Deleted Contribution",
                Subtitle = contrib?.Title,
                Route = $"/contribution/{idEncoder.Encode(t.ContributionId)}/messages",
                LastMessagePreview = Trim(t.LastMessageText),
                LastMessageAt = t.LastMessageAt,
                UnreadCount = t.UnreadCount,
                TotalCount = t.TotalCount,
            });
        }

        foreach (var t in boxsetThreads)
        {
            var title = boxsets.GetValueOrDefault(t.BoxsetId) ?? "Deleted Boxset";
            threads.Add(new MessageThread
            {
                Kind = MessageThreadKind.Boxset,
                EntityId = t.BoxsetId,
                EncodedEntityId = idEncoder.Encode(t.BoxsetId),
                Title = title,
                Subtitle = "Boxset contribution",
                Route = $"/contribution/boxset/{idEncoder.Encode(t.BoxsetId)}/messages",
                LastMessagePreview = Trim(t.LastMessageText),
                LastMessageAt = t.LastMessageAt,
                UnreadCount = t.UnreadCount,
                TotalCount = t.TotalCount,
            });
        }

        foreach (var t in suggestionThreads)
        {
            if (!suggestions.TryGetValue(t.EditSuggestionId, out var suggestion))
            {
                continue;
            }

            var encodedId = idEncoder.Encode(t.EditSuggestionId);
            threads.Add(new MessageThread
            {
                Kind = MessageThreadKind.EditSuggestion,
                EntityId = t.EditSuggestionId,
                EncodedEntityId = encodedId,
                Title = suggestion.Summary ?? $"Suggested edit to {suggestion.TargetEntityType}",
                Subtitle = suggestion.TargetEntityKey,
                Route = isAdmin
                    ? $"/admin/changes/{t.EditSuggestionId}"
                    : $"/changes/my/{encodedId}",
                LastMessagePreview = Trim(t.LastMessageText),
                LastMessageAt = t.LastMessageAt,
                UnreadCount = t.UnreadCount,
                TotalCount = t.TotalCount,
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
