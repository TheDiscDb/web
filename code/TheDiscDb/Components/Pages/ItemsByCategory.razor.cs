using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class ItemsByCategory : ComponentBase
{
    [Parameter]
    public string? Slug { get; set; }

    [Inject]
    IDbContextFactory<SqlServerDataContext>? Context { get; set; }

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private bool groupExists;
    private string? groupName;

    protected override async Task OnInitializedAsync()
    {
        if (Context != null && !string.IsNullOrEmpty(Slug))
        {
            var context = await Context.CreateDbContextAsync();
            var group = await context.Groups
                .FirstOrDefaultAsync(g => g.Slug == Slug);
            groupExists = group != null;
            groupName = group?.Name;
        }

        if (!groupExists && HttpContext != null && !HttpContext.Response.HasStarted)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
