using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class Review : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    //protected override async Task OnInitializedAsync()
    //{
        
    //}
}
