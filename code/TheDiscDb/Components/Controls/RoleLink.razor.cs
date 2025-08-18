using Microsoft.AspNetCore.Components;
using TheDiscDb.InputModels;

namespace TheDiscDb.Components.Controls;

public partial class RoleLink : ComponentBase
{
    [Parameter]
    public string? Text { get; set; }

    [Parameter]
    public string? Role { get; set; }

    [Parameter]
    public IDisplayItem? Item { get; set; }

    private bool IsMediaItem => Item is MediaItem;
    private MediaItem? MediaItem => Item as MediaItem;
}
