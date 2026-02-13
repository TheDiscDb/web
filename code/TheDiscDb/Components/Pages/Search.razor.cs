using System.Web;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Search;

namespace TheDiscDb.Components.Pages
{
    public partial class Search : ComponentBase
    {
        [Parameter]
        public string? Query { get; set; }

        [SupplyParameterFromQuery(Name = "q")]
        public string? Q { get; set; }

        [Inject]
        public ISearchService SearchService { get; set; } = null!;

        public IEnumerable<SearchEntry>? Results { get; set; }

        public string SearchTerm => Q ?? HttpUtility.UrlDecode(Query ?? string.Empty);

        protected override async Task OnInitializedAsync()
        {
            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                Results = await SearchService.Search(SearchTerm);
            }
        }
    }
}
