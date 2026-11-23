using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class DiscFolderPickerFallback
{
    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback OnPickFolder { get; set; }
}