using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.Achievements;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class Leaderboard : ComponentBase, IDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "showAll")]
    public bool ShowAll { get; set; }

    private readonly CancellationTokenSource cts = new();
    private CancellationToken ComponentCt => this.cts.Token;

    private List<LeaderboardEntry>? leaders;
    private bool showAll;
    private string? errorMessage;

    private const string ExcludedContributorName = "lfoust";
    private const int MaxBadgesPerRow = 3;

    protected override async Task OnInitializedAsync()
    {
        showAll = ShowAll;
        await LoadData();
    }

    private async Task LoadData()
    {
        try
        {
            errorMessage = null;
            await using var db = await DbFactory.CreateDbContextAsync(this.ComponentCt);

            var query = db.Contributors.AsQueryable();

            if (!showAll)
            {
                query = query.Where(c => c.Name != ExcludedContributorName);
            }

            var baseRows = await query
                .Where(c => c.Releases.Any())
                .Select(c => new
                {
                    Name = c.Name ?? "Anonymous",
                    ReleaseCount = c.Releases.Count,
                    LatestRelease = c.Releases.Max(r => r.DateAdded)
                })
                .OrderByDescending(e => e.ReleaseCount)
                .ThenByDescending(e => e.LatestRelease)
                .ToListAsync(this.ComponentCt);

            var badgesByName = await LoadBadgesAsync(db, baseRows.Select(r => r.Name));

            leaders = baseRows
                .Select(r =>
                {
                    badgesByName.TryGetValue(r.Name.ToUpperInvariant(), out var info);
                    return new LeaderboardEntry
                    {
                        Name = r.Name,
                        ReleaseCount = r.ReleaseCount,
                        LatestRelease = r.LatestRelease,
                        Level = info?.Level,
                        Badges = info?.Badges ?? Array.Empty<LeaderboardBadge>()
                    };
                })
                .ToList();
        }
        catch
        {
            errorMessage = "Unable to load leaderboard data. Please try again later.";
            leaders = [];
        }
    }

    /// <summary>
    /// Resolves each contributor (by GitHub login) to its authenticated account's level and top
    /// earned badges. Keyed by upper-cased name to match on <c>NormalizedUserName</c>.
    /// </summary>
    private async Task<Dictionary<string, ContributorBadgeInfo>> LoadBadgesAsync(
        SqlServerDataContext db, IEnumerable<string> names)
    {
        var normalized = names.Select(n => n.ToUpperInvariant()).Distinct().ToList();

        var users = await db.Users
            .Where(u => u.NormalizedUserName != null && normalized.Contains(u.NormalizedUserName))
            .Select(u => new { u.Id, u.NormalizedUserName, u.Level, u.TotalPoints })
            .ToListAsync(this.ComponentCt);

        if (users.Count == 0)
        {
            return new Dictionary<string, ContributorBadgeInfo>();
        }

        var userIds = users.Select(u => u.Id).ToList();
        var earned = await db.UserAchievements
            .Where(a => userIds.Contains(a.UserId))
            .Select(a => new { a.UserId, a.AchievementKey })
            .ToListAsync(this.ComponentCt);

        var keysByUserId = earned
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.AchievementKey).ToList());

        var result = new Dictionary<string, ContributorBadgeInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            if (user.NormalizedUserName is null)
            {
                continue;
            }

            var badges = keysByUserId.TryGetValue(user.Id, out var keys)
                ? keys
                    .Select(AchievementRegistry.Find)
                    .Where(d => d is not null && !d!.IsActivityOnly)
                    .OrderByDescending(d => d!.Points)
                    .Take(MaxBadgesPerRow)
                    .Select(d => new LeaderboardBadge(d!.Icon, d.Tier, d.Name))
                    .ToList()
                : new List<LeaderboardBadge>();

            var level = string.IsNullOrEmpty(user.Level) ? LevelCalculator.ForPoints(user.TotalPoints) : user.Level;
            result[user.NormalizedUserName] = new ContributorBadgeInfo(level, badges);
        }

        return result;
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }

    private sealed record ContributorBadgeInfo(string Level, IReadOnlyList<LeaderboardBadge> Badges);

    private sealed record LeaderboardBadge(string Icon, AchievementTier Tier, string Name);

    private record LeaderboardEntry
    {
        public string Name { get; init; } = "Anonymous";
        public int ReleaseCount { get; init; }
        public DateTimeOffset LatestRelease { get; init; }
        public string? Level { get; init; }
        public IReadOnlyList<LeaderboardBadge> Badges { get; init; } = Array.Empty<LeaderboardBadge>();
    }
}
