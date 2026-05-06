using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class ItemsByCategory : ComponentBase, IDisposable
{
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

    protected override async Task OnInitializedAsync()
    {
        if (Context != null && !string.IsNullOrEmpty(Slug))
        {
            var context = await Context.CreateDbContextAsync(this.ComponentCt);
            var group = await context.Groups
                .FirstOrDefaultAsync(g => g.Slug == Slug, this.ComponentCt);
            groupExists = group != null;
            groupName = group?.Name;
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
