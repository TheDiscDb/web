using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Controls;
using TheDiscDb.Components.InfiniteScrolling;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages;

public partial class Boxsets : ComponentBase
{
    private InfiniteScrolling<IGetBoxsets_Boxsets_Nodes>? infiniteScrolling;
    private SortItemDefinition<BoxsetSortInput>? selectedSortDefinition;

    [Inject]
    public GetBoxsetsQuery? Query { get; set; }

    public IPageInfo? PageInfo { get; set; }

    IReadOnlyList<BoxsetSortInput> OrderBy { get; set; } = new List<BoxsetSortInput>();

    List<SortItemDefinition<BoxsetSortInput>> SortItemDefinitions { get; set; } = new List<SortItemDefinition<BoxsetSortInput>>
        {
            new SortItemDefinition<BoxsetSortInput>
            {
                Id = "2",
                IsDefault = true,
                DisplayText = "Release Date",
                Default = SortEnumType.Desc,
                Ascending = new BoxsetSortInput
                {
                    Release = new ReleaseSortInput
                    {
                        ReleaseDate = SortEnumType.Asc
                    }
                },
                Descending = new BoxsetSortInput
                {
                    Release = new ReleaseSortInput
                    {
                        ReleaseDate = SortEnumType.Desc
                    }
                }
            },
            new SortItemDefinition<BoxsetSortInput>
            {
                Id = "3",
                DisplayText = "Date Added",
                Default = SortEnumType.Desc,
                Ascending = new BoxsetSortInput
                {
                    Release = new ReleaseSortInput
                    {
                        DateAdded = SortEnumType.Asc
                    }
                },
                Descending = new BoxsetSortInput
                {
                    Release = new ReleaseSortInput
                    {
                        DateAdded = SortEnumType.Desc
                    }
                }
            },
            new SortItemDefinition<BoxsetSortInput>
            {
                Id = "4",
                DisplayText = "Title",
                Default = SortEnumType.Asc,
                Ascending = new BoxsetSortInput
                {
                    SortTitle = SortEnumType.Asc
                },
                Descending = new BoxsetSortInput
                {
                    SortTitle = SortEnumType.Desc
                }
            }
        };

    protected override void OnInitialized()
    {
        base.OnInitialized();
        var defaultSort = this.SortItemDefinitions.FirstOrDefault(x => x.IsDefault);
        this.selectedSortDefinition = defaultSort;
        var sortType = defaultSort?.Default ?? SortEnumType.Asc;
        if (this.selectedSortDefinition != null)
        {
           this.OrderBy = this.selectedSortDefinition.GetOrderBy(sortType);
        }
    }

    public async Task<IEnumerable<IGetBoxsets_Boxsets_Nodes>> GetItems(InfiniteScrollingItemsProviderRequest request)
    {
        IOperationResult<IGetBoxsetsResult>? result = null;

        if (this.PageInfo == null)
        {
            result = await this.Query!.ExecuteAsync(after: null, order: this.OrderBy, request.CancellationToken);
        }
        else if (this.PageInfo.HasNextPage)
        {
            result = await this.Query!.ExecuteAsync(after: this.PageInfo.EndCursor, order: this.OrderBy, request.CancellationToken);
        }

        if (result != null && result.IsSuccessResult())
        {
            this.PageInfo = result.Data!.Boxsets!.PageInfo!;
            return result.Data!.Boxsets!.Nodes!;
        }

        return Enumerable.Empty<IGetBoxsets_Boxsets_Nodes>();
    }

    public async Task SortDefinitionChanged((SortItemDefinition<BoxsetSortInput> Definition, SortEnumType Direction) e)
    {
        if (this.selectedSortDefinition == null || this.selectedSortDefinition != e.Definition)
        {
            this.OrderBy = e.Definition.GetOrderBy(e.Definition.Default);
            this.selectedSortDefinition = e.Definition;
        }
        else
        {
            this.OrderBy = e.Definition.GetOrderBy(e.Direction);
        }

        this.PageInfo = null;
        var task = this.infiniteScrolling?.Refresh();
        if (task != null)
        {
            await task;
        }
    }
}
