namespace TheDiscDb.Client.Controls;

using Microsoft.AspNetCore.Components;
using TheDiscDb.Client.Services;

public partial class DiscSearchResults
{
    [Parameter, EditorRequired] public DiscSearchResult Result { get; set; } = default!;
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public EventCallback<DiscTitleMatch> OnAddRelease { get; set; }
    [Parameter] public EventCallback OnSubmit { get; set; }
}
