namespace TheDiscDb.Components.Pages.Admin;

using Microsoft.AspNetCore.Components;
using TheDiscDb.Contributions.OpticalDiscManifest;

public partial class DiscScanTitleDifferenceTable
{
    [Parameter]
    public string Title { get; set; } = string.Empty;

    [Parameter]
    public IReadOnlyList<TitleDifference> Rows { get; set; } = [];
}
