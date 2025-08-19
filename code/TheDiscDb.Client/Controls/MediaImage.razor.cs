using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Controls;

public partial class MediaImage : ComponentBase
{
    [Parameter]
    public string? Url { get; set; }

    [Parameter]
    public int Width { get; set; }

    [Parameter]
    public int Height { get; set; }

    [Parameter]
    public string? Title { get; set; }

    private string FullImageUrl => $"/images/{Url}?width={Width}&height={Height}";
}
