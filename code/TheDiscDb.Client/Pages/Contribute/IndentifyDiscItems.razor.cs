using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class IdentifyDiscItems : ComponentBase
{
    [Inject]
    public ApiClient HashClient { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }
}
