using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class Leaderboard : ComponentBase
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "showAll")]
    public bool ShowAll { get; set; }

    private List<LeaderboardEntry>? leaders;
    private bool showAll;
    private string? errorMessage;

    private const string ExcludedContributorName = "lfoust";

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
            await using var db = await DbFactory.CreateDbContextAsync();

            var query = db.Contributors.AsQueryable();

            if (!showAll)
            {
                query = query.Where(c => c.Name != ExcludedContributorName);
            }

            leaders = await query
                .Where(c => c.Releases.Any())
                .Select(c => new LeaderboardEntry
                {
                    Name = c.Name ?? "Anonymous",
                    ReleaseCount = c.Releases.Count,
                    LatestRelease = c.Releases.Max(r => r.DateAdded)
                })
                .OrderByDescending(e => e.ReleaseCount)
                .ThenByDescending(e => e.LatestRelease)
                .ToListAsync();
        }
        catch
        {
            errorMessage = "Unable to load leaderboard data. Please try again later.";
            leaders = [];
        }
    }

    private record LeaderboardEntry
    {
        public string Name { get; init; } = "Anonymous";
        public int ReleaseCount { get; init; }
        public DateTimeOffset LatestRelease { get; init; }
    }
}
