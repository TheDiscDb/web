using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Controls;

public partial class Breadcrumb : ComponentBase
{
    [Parameter]
    public IEnumerable<(string Text, string Url)> Items { get; set; } = new List<(string Text, string Url)>();

    [Parameter]
    public string? PrimaryText { get; set; }
}
