using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class IdentifyTitle : ComponentBase
{
    [Inject]
    public TmdbDataAdaptor? TmdbAdaptor { get; set; }

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public string? MediaType { get; set; }

    public string? SearchText { get; set; }

    public bool IsNextButtonDisabled => SelectedItem == null;

    private MediaItem? SelectedItem { get; set; }

    override protected void OnInitialized()
    {
        if (TmdbAdaptor != null)
        {
            TmdbAdaptor.MediaType = MediaType;
        }

        base.OnInitialized();
    }

    string GetItemDisplayText(MediaItem item)
    {
        return $"{item.Title} ({item.Year})";
    }

    private void NextClicked(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        NavigationManager.NavigateTo($"/contribute/{MediaType}/{SelectedItem!.Id}");
    }
}
