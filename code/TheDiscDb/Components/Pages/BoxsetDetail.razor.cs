using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class BoxsetDetail : ComponentBase, IDisposable
{
    [Parameter]
    public string? Slug { get; set; }

    [Inject]
    IDbContextFactory<SqlServerDataContext>? Context { get; set; }

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private readonly CancellationTokenSource cts = new();
    private CancellationToken ComponentCt => this.cts.Token;

    private Boxset? Item { get; set; }
    private Release? Release { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (this.Context == null)
        {
            throw new Exception("Context was not injected");
        }

        var context = await this.Context.CreateDbContextAsync(this.ComponentCt);
        Item = await context.BoxSets
            .Include("Release")
            .Include("Release.Discs")
            .Include("Release.Discs.Titles")
            .Include("Release.Discs.Titles.Item")
            .FirstOrDefaultAsync(i => i.Slug == Slug, this.ComponentCt);
        if (Item != null)
        {
            Release = Item.Release;
        }

        if (Item == null || Release == null)
        {
            if (HttpContext != null && !HttpContext.Response.HasStarted)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }
}