using Microsoft.AspNetCore.Identity;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IContributionHistoryService
{
    Task RecordCreatedAsync(int contributionId, string userId, CancellationToken cancellationToken = default);
    Task RecordStatusChangedAsync(int contributionId, string userId, UserContributionStatus oldStatus, UserContributionStatus newStatus, CancellationToken cancellationToken = default);
    Task RecordDeletedAsync(int contributionId, string userId, CancellationToken cancellationToken = default);
    Task AddMessageAsync(int contributionId, string userId, string message, ContributionHistoryType type, CancellationToken cancellationToken = default);
}

public class ContributionHistoryService(SqlServerDataContext database) : IContributionHistoryService
{
    public async Task RecordCreatedAsync(int contributionId, string userId, CancellationToken cancellationToken = default)
    {
        database.ContributionHistory.Add(new ContributionHistory
        {
            ContributionId = contributionId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = "Contribution created",
            UserId = userId,
            Type = ContributionHistoryType.Created
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordStatusChangedAsync(int contributionId, string userId, UserContributionStatus oldStatus, UserContributionStatus newStatus, CancellationToken cancellationToken = default)
    {
        database.ContributionHistory.Add(new ContributionHistory
        {
            ContributionId = contributionId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = $"Status changed from **{oldStatus}** to **{newStatus}**",
            UserId = userId,
            Type = ContributionHistoryType.StatusChanged
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordDeletedAsync(int contributionId, string userId, CancellationToken cancellationToken = default)
    {
        database.ContributionHistory.Add(new ContributionHistory
        {
            ContributionId = contributionId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = "Contribution deleted",
            UserId = userId,
            Type = ContributionHistoryType.Deleted
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task AddMessageAsync(int contributionId, string userId, string message, ContributionHistoryType type, CancellationToken cancellationToken = default)
    {
        if (type != ContributionHistoryType.AdminMessage && type != ContributionHistoryType.UserMessage)
        {
            throw new ArgumentException("Type must be AdminMessage or UserMessage", nameof(type));
        }

        database.ContributionHistory.Add(new ContributionHistory
        {
            ContributionId = contributionId,
            TimeStamp = DateTimeOffset.UtcNow,
            Description = message,
            UserId = userId,
            Type = type
        });
        await database.SaveChangesAsync(cancellationToken);
    }
}
