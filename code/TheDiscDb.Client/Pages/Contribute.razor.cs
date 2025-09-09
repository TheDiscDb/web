using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages;

public partial class Contribute : ComponentBase
{
    [Inject]
    public NavigationManager? Navigation { get; set; }

    public string? TmdbId { get; set; }

    private void NextButtonClicked(Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
    {
        if (TmdbId != null)
        {
            this.Navigation!.NavigateTo("/contribute/" +  TmdbId);
        }
    }
}
