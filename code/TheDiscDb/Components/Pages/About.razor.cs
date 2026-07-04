using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class About : ComponentBase
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IMemoryCache Cache { get; set; } = null!;

    private SiteStats? stats;

    protected override async Task OnInitializedAsync()
    {
        stats = await this.Cache.GetOrCreateAsync("about-page-stats", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            try
            {
                await using var db = await this.DbFactory.CreateDbContextAsync();
                var movies = await db.MediaItems.CountAsync(m => m.Type == "Movie");
                var series = await db.MediaItems.CountAsync(m => m.Type == "Series");
                var boxsets = await db.BoxSets.CountAsync();
                var discs = await db.Discs.CountAsync();
                var contributors = await db.Contributors.CountAsync(c => c.Releases.Any());
                return new SiteStats(movies, series, boxsets, discs, contributors);
            }
            catch
            {
                // Retry quickly after a transient DB error rather than caching a stale null for hours.
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return null;
            }
        });
    }

    private record SiteStats(int Movies, int Series, int Boxsets, int Discs, int Contributors);
}
