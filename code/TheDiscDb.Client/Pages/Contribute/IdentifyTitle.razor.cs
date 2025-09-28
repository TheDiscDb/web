using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class IdentifyTitle : ComponentBase
{
    [Parameter]
    public string? MediaType { get; set; }

    public string? SearchText { get; set; }

    public readonly List<IDisplayItem> foundItems = new ();

    //protected override async Task OnInitializedAsync()
    //{
        
    //}
}
