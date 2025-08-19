using Microsoft.AspNetCore.Components;
using TheDiscDb.InputModels;

namespace TheDiscDb.Components.Controls;

public partial class GroupList : ComponentBase
{
    [Parameter]
    public string? ListText { get; set; }

    [Parameter]
    public string? Role { get; set; }

    [Parameter]
    public IDisplayItem? Item { get; set; }
}
