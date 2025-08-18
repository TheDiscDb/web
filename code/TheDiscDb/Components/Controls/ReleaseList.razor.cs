using Microsoft.AspNetCore.Components;
using TheDiscDb.InputModels;

namespace TheDiscDb.Components.Controls;

public partial class ReleaseList : ComponentBase
{
    [Parameter]
    public IDisplayItem? Item { get; set; }

    [Parameter]
    public IEnumerable<Release> Releases { get; set; } = new List<Release>();
}
