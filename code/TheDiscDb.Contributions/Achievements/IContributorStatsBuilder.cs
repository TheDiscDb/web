namespace TheDiscDb.Services.Achievements;

using System.Threading;
using System.Threading.Tasks;

/// <summary>Builds the per-user <see cref="ContributorStats"/> snapshot from the database.</summary>
public interface IContributorStatsBuilder
{
    Task<ContributorStats> BuildAsync(string userId, CancellationToken cancellationToken = default);
}
