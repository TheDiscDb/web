using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;
using HotChocolate.Authorization;

namespace TheDiscDb.GraphQL.Contribute;

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
}
