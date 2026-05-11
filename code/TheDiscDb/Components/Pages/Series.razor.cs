using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class Series : ComponentBase
{
    [Inject]
    IDbContextFactory<SqlServerDataContext>? Context { get; set; }

    [Inject]
    IMemoryCache? Cache { get; set; }

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private bool isCrawler;
    private IReadOnlyList<MediaItem> ssrItems = Array.Empty<MediaItem>();

    protected override async Task OnInitializedAsync()
    {
        isCrawler = CrawlerDetector.IsCrawler(HttpContext);
        if (isCrawler && Context != null && Cache != null)
        {
            ssrItems = await CrawlerBrowseSsr.GetSeriesAsync(Context, Cache, HttpContext?.RequestAborted ?? CancellationToken.None);
        }
    }
}
