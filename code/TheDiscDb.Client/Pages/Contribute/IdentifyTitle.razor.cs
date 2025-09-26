using Microsoft.AspNetCore.Components;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class IdentifyTitle : ComponentBase
{
    [Parameter]
    public string? Hash { get; set; }

    [Parameter]
    public string? MediaType { get; set; }

    public string? SearchText { get; set; }

    public readonly List<IDisplayItem> foundItems = new ();

    //protected override async Task OnInitializedAsync()
    //{
        
    //}
}
