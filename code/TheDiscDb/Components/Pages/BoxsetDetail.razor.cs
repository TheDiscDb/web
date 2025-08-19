using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class BoxsetDetail : ComponentBase
{
    [Parameter]
    public string? Slug { get; set; }

    [Inject]
    IDbContextFactory<SqlServerDataContext>? Context { get; set; }

    private Boxset? Item { get; set; }
    private Release? Release { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (this.Context == null)
        {
            throw new Exception("Context was not injected");
        }

        var context = await this.Context.CreateDbContextAsync();
        Item = context.BoxSets
            .Include("Release")
            .Include("Release.Discs")
            .Include("Release.Discs.Titles")
            .Include("Release.Discs.Titles.Item")
            .FirstOrDefault(i => i.Slug == Slug);
        if (Item != null)
        {
            Release = Item.Release;
        }
    }
}