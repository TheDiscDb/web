using System.Security.Claims;
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
    public IQueryable<ContributionHistory> GetContributionChat(SqlServerDataContext context, string contributionId, ClaimsPrincipal user)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Enumerable.Empty<ContributionHistory>().AsQueryable();
        }

        var decodedId = idEncoder.Decode(contributionId);

        // Verify the user owns this contribution
        var ownsContribution = context.UserContributions.Any(c => c.Id == decodedId && c.UserId == userId);
        if (!ownsContribution)
        {
            return Enumerable.Empty<ContributionHistory>().AsQueryable();
        }

        return context.ContributionHistory
            .Where(h => h.ContributionId == decodedId &&
                (h.Type == ContributionHistoryType.AdminMessage ||
                 h.Type == ContributionHistoryType.UserMessage ||
                 h.Type == ContributionHistoryType.StatusChanged))
            .OrderByDescending(h => h.TimeStamp);
    }
}
