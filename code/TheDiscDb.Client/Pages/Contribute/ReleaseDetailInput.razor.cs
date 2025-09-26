using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class ReleaseDetailInput : ComponentBase
{
    [Parameter]
    public string? Hash { get; set; }

    [Parameter]
    public string? MediaType { get; set; }

    [Parameter]
    public string? TmdbId { get; set; }

    //protected override async Task OnInitializedAsync()
    //{
        
    //}
}
