using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Data.GraphQL;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages;

public partial class ItemsByCategory : ComponentBase
{
    [Parameter]
    public string? Slug { get; set; }

    [Inject]
    public GetMediaItemsByGroupQuery? Query { get; set; }

    public IPageInfo? PageInfo { get; set; }

    IReadOnlyList<MediaItemSortInput> OrderBy { get; set; } = new List<MediaItemSortInput>();


    private string? name = null;
    public string? Name
    {
        get
        {
            if (string.IsNullOrEmpty(name))
            {
                return Slug;
            }

            return name;
        }
    }

    public async Task<IEnumerable<IGetMediaItemsByGroup_MediaItemsByGroup_Nodes>> GetItems(InfiniteScrollingItemsProviderRequest request)
    {
        IOperationResult<IGetMediaItemsByGroupResult>? result = null;

        if (this.PageInfo == null)
        {
            result = await this.Query!.ExecuteAsync(this.Slug!, role: null, after: null, order: this.OrderBy, request.CancellationToken);
        }
        else if (this.PageInfo.HasNextPage)
        {
            result = await this.Query!.ExecuteAsync(this.Slug!, role: null, after: this.PageInfo.EndCursor, order: this.OrderBy, request.CancellationToken);
        }

        if (result != null && result.IsSuccessResult())
        {
            this.PageInfo = result.Data!.MediaItemsByGroup!.PageInfo!;

            if (string.IsNullOrEmpty(this.name))
            {
                this.name = result.Data.MediaItemsByGroup.Nodes?.FirstOrDefault()?.MediaItemGroups?.FirstOrDefault()?.Group?.Name;
                this.StateHasChanged();
            }

            return result.Data!.MediaItemsByGroup!.Nodes!;
        }

        return Enumerable.Empty<IGetMediaItemsByGroup_MediaItemsByGroup_Nodes>();
    }
}
