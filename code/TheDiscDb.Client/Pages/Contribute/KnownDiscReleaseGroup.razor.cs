namespace TheDiscDb.Client.Pages.Contribute;

using Microsoft.AspNetCore.Components;
using TheDiscDb.Client.Services;

public partial class KnownDiscReleaseGroup
{
    [Parameter, EditorRequired] public DiscTitleMatch Title { get; set; } = default!;
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public EventCallback<DiscTitleMatch> OnAddRelease { get; set; }
}