using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class Boxsets : ComponentBase
{
    [Inject]
    IDbContextFactory<SqlServerDataContext>? Context { get; set; }

    [Inject]
    IMemoryCache? Cache { get; set; }

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private bool isCrawler;
    private IReadOnlyList<Boxset> ssrItems = Array.Empty<Boxset>();

    protected override async Task OnInitializedAsync()
    {
        isCrawler = CrawlerDetector.IsCrawler(HttpContext);
        if (isCrawler && Context != null && Cache != null)
        {
            ssrItems = await CrawlerBrowseSsr.GetBoxsetsAsync(Context, Cache, HttpContext?.RequestAborted ?? CancellationToken.None);
        }
    }
}
