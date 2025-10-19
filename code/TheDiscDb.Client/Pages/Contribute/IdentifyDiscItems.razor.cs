using MakeMkv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Buttons;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class IdentifyDiscItems : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IUserContributionService Client { get; set; } = null!;

    private IQueryable<MakeMkv.Title>? titles = null;
    private UserContributionDisc? disc = null;
    private readonly Dictionary<Title, bool> identifiedTitles = new Dictionary<Title, bool>(); 

    protected override async Task OnInitializedAsync()
    {
        var response = await this.Client.GetDiscLogs(this.ContributionId!, this.DiscId!);
        if (response?.Value != null)
        {
            this.titles = response.Value.Info!.Titles.AsQueryable();
            this.disc = response.Value.Disc;
        }
    }

    private IconName GetIcon(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out bool identified) && identified)
        {
            return IconName.CircleRemove;
        }

        return IconName.CircleAdd;
    }

    private void IdentifyItemClicked(Title title, string type)
    {
        // TODO: Save title on server

        this.identifiedTitles[title] = true;
        this.StateHasChanged();
    }
}
