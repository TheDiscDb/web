using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Controls;
using TheDiscDb.Components.InfiniteScrolling;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages
{
    public partial class Series : ComponentBase
    {
        private InfiniteScrolling<IGetSeries_MediaItems_Nodes>? infiniteScrolling;
        private SortItemDefinition<MediaItemSortInput>? selectedSortDefinition;

        [Inject]
        public GetSeriesQuery? Query { get; set; }

        public IPageInfo? PageInfo { get; set; }

        IReadOnlyList<MediaItemSortInput> OrderBy { get; set; } = new List<MediaItemSortInput>();

        List<SortItemDefinition<MediaItemSortInput>> SortItemDefinitions { get; set; } = new List<SortItemDefinition<MediaItemSortInput>>
        {
            new SortItemDefinition<MediaItemSortInput>
            {
                Id = "1",
                IsDefault = true,
                DisplayText = "Latest Release",
                Default = SortEnumType.Desc,
                Ascending = new MediaItemSortInput
                {
                    LatestReleaseDate = SortEnumType.Asc
                },
                Descending = new MediaItemSortInput
                {
                    LatestReleaseDate = SortEnumType.Desc
                }
            },
            new SortItemDefinition<MediaItemSortInput>
            {
                Id = "2",
                DisplayText = "Release Date",
                Default = SortEnumType.Desc,
                Ascending = new MediaItemSortInput
                {
                    ReleaseDate = SortEnumType.Asc
                },
                Descending = new MediaItemSortInput
                {
                    ReleaseDate = SortEnumType.Desc
                }
            },
            new SortItemDefinition<MediaItemSortInput>
            {
                Id = "3",
                DisplayText = "Date Added",
                Default = SortEnumType.Desc,
                Ascending = new MediaItemSortInput
                {
                    DateAdded = SortEnumType.Asc
                },
                Descending = new MediaItemSortInput
                {
                    DateAdded = SortEnumType.Desc
                }
            },
            new SortItemDefinition<MediaItemSortInput>
            {
                Id = "4",
                DisplayText = "Title",
                Default = SortEnumType.Asc,
                Ascending = new MediaItemSortInput
                {
                    SortTitle = SortEnumType.Asc
                },
                Descending = new MediaItemSortInput
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
            var sort = defaultSort?.Default ?? SortEnumType.Asc;

            if (this.selectedSortDefinition != null)
            {
                this.OrderBy = this.selectedSortDefinition.GetOrderBy(sort);
            }
        }

        public async Task<IEnumerable<IGetSeries_MediaItems_Nodes>> GetItems(InfiniteScrollingItemsProviderRequest request)
        {
            IOperationResult<IGetSeriesResult>? result = null;

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
                this.PageInfo = result.Data!.MediaItems!.PageInfo!;
                return result.Data!.MediaItems!.Nodes!;
            }

            return Enumerable.Empty<IGetSeries_MediaItems_Nodes>();
        }

        public async Task SortDefinitionChanged((SortItemDefinition<MediaItemSortInput> Definition, SortEnumType Direction) e)
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
}