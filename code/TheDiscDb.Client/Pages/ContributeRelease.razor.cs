using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages;

public partial class ContributeRelease : ComponentBase
{
    [Parameter]
    public string? TmdbId { get; set; }
}
