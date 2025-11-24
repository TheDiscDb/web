using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class IdentifyTitle : ComponentBase
{
    [Inject]
    public ExternalSearchDataAdaptor? DataAdaptor { get; set; }

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public string? MediaType { get; set; }

    string BreadcrumbText => $"Find {MediaType}";

    public string? SearchText { get; set; }

    public bool IsNextButtonDisabled => SelectedItem == null;

    private ExternalSearchResult? SelectedItem { get; set; }

    override protected void OnInitialized()
    {
        if (DataAdaptor != null)
        {
            DataAdaptor.MediaType = MediaType;
        }

        base.OnInitialized();
    }

    string GetItemDisplayText(ExternalSearchResult item)
    {
        return $"{item.Title} ({item.Year})";
    }

    private void NextClicked(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        NavigationManager.NavigateTo($"/contribute/{MediaType}/{SelectedItem!.Id}");
    }
}
