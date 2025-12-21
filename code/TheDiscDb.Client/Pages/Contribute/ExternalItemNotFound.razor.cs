using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ExternalItemNotFound : ComponentBase
{
    [Parameter]
    public string? ExternalId { get; set; }
}
