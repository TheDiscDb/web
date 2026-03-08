using Microsoft.AspNetCore.Identity;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public interface IContributionHistoryService
{
    Task RecordCreatedAsync(int contributionId, string userId, CancellationToken cancellationToken = default);
    Task RecordStatusChangedAsync(int contributionId, string userId, UserContributionStatus oldStatus, UserContributionStatus newStatus, CancellationToken cancellationToken = default);
    Task RecordDeletedAsync(int contributionId, string userId, CancellationToken cancellationToken = default);
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

}
