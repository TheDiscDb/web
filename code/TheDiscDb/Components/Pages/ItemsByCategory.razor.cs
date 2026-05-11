using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class ItemsByCategory : ComponentBase, IDisposable
{
    // Cap for SEO-visible prerendered list. Keep this generous enough that
    // most groups render their full filmography but small enough to avoid
    // pathologically large pages for major studios.
    private const int InitialItemLimit = 100;

    [Parameter]
    public string? Slug { get; set; }

    [Inject]
    IDbContextFactory<SqlServerDataContext>? Context { get; set; }

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private readonly CancellationTokenSource cts = new();
    private CancellationToken ComponentCt => this.cts.Token;

    private bool groupExists;
    private string? groupName;
    private bool isCrawler;
    private IReadOnlyList<MediaItem> initialItems = Array.Empty<MediaItem>();
    private bool hasMoreItems;

    protected override async Task OnInitializedAsync()
    {
        isCrawler = CrawlerDetector.IsCrawler(HttpContext);

        if (Context != null && !string.IsNullOrEmpty(Slug))
        {
            await using var context = await Context.CreateDbContextAsync(this.ComponentCt);
            var group = await context.Groups
                .FirstOrDefaultAsync(g => g.Slug == Slug, this.ComponentCt);
            groupExists = group != null;
            groupName = group?.Name;

            if (groupExists && isCrawler)
            {
                // Only run the (relatively heavy) item query for crawlers — humans
                // get the WASM-driven InfiniteScrolling experience instead. Mirrors
                // the GraphQL `mediaItemsByGroup` resolver so both paths stay equivalent.
                var query = context.MediaItems
                    .AsNoTracking()
                    .Where(i =>
                        i.MediaItemGroups.Any(g => g.Group != null && g.Group.Slug == Slug) ||
                        i.Releases.Any(r => r.ReleaseGroups.Any(rg => rg.Group != null && rg.Group.Slug == Slug)))
                    .OrderBy(i => i.SortTitle ?? i.Title)
                    .ThenBy(i => i.Year);

                // Fetch one extra so we know whether to show a "more available" hint
                // without a separate COUNT query.
                var fetched = await query
                    .Take(InitialItemLimit + 1)
                    .Select(i => new MediaItem
                    {
                        Slug = i.Slug,
                        Title = i.Title,
                        Year = i.Year,
                        Type = i.Type,
                        ImageUrl = i.ImageUrl,
                    })
                    .ToListAsync(this.ComponentCt);

                hasMoreItems = fetched.Count > InitialItemLimit;
                initialItems = hasMoreItems ? fetched.Take(InitialItemLimit).ToList() : fetched;
            }
        }

        if (!groupExists && HttpContext != null && !HttpContext.Response.HasStarted)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }
}
