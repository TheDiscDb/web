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

    public bool IsNextButtonDisabled => SelectedItem == null && (string.IsNullOrEmpty(ManualTmdbId) || !ulong.TryParse(ManualTmdbId, out _));

    private ExternalSearchResult? SelectedItem { get; set; }

    private string? ManualTmdbId { get; set; }

    private string PluralMediaType
    {
        get
        {
            if (!string.IsNullOrEmpty(this.MediaType))
            {
                if (this.MediaType.Equals("movie", StringComparison.OrdinalIgnoreCase))
                {
                    return "Movies";
                }
                else if (this.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
                {
                    return "Shows";
                }
            }

            return this.MediaType ?? "Media";
        }
    }

    private string SwitchButtonText
    {
        get
        {
            if (string.IsNullOrEmpty(this.MediaType) || this.MediaType.Equals("movie", StringComparison.OrdinalIgnoreCase))
            {
                return "Switch to Series search";
            }

            return "Switch to Movie search";
        }
    }



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
        string? selectedId = this.ManualTmdbId;
        if (this.SelectedItem != null)
        {
            selectedId = this.SelectedItem.Id.ToString();
        }

        if (!string.IsNullOrEmpty(selectedId))
        {
            NavigationManager.NavigateTo($"/contribute/{MediaType}/{selectedId}");
        }
    }
    private void SwitchMediaType(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        string otherMediaType = "Series";
        if (string.IsNullOrEmpty(this.MediaType) || this.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            otherMediaType = "Movie";
        }

        NavigationManager.NavigateTo($"/contribute/{otherMediaType}");
    }
}
