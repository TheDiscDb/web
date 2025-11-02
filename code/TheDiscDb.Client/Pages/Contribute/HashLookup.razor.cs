using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Data.GraphQL;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class HashLookup : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? Hash { get; set; }

    [Inject]
    public GetDiscDetailByContentHashQuery? Query { get; set; }

    public readonly List<IDisplayItem> foundItems = new ();

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrEmpty(this.Hash))
        {
            // TODO: Get a cancellation token
            var result = await Query!.ExecuteAsync(this.Hash);
            if (result.Data?.MediaItems?.Nodes != null)
            {
                foreach (var mediaItem in result.Data.MediaItems.Nodes)
                {
                    foundItems.Add(mediaItem);
                }
            }
        }
    }
}
