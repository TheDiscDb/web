using System.Web;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Search;

namespace TheDiscDb.Components.Pages
{
    public partial class Search : ComponentBase
    {
        [Parameter]
        public string Query { get; set; } = string.Empty;

        [Inject]
        public ISearchService SearchService { get; set; } = null!;

        public IEnumerable<SearchEntry>? Results { get; set; }

        protected override async Task OnInitializedAsync()
        {
            this.Results = await this.SearchService.Search(HttpUtility.UrlDecode(Query));
            this.StateHasChanged();
        }
    }
}
