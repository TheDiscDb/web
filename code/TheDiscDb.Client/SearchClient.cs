namespace TheDiscDb.Client
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using TheDiscDb.Search;

    public class SearchClient
    {
        private readonly HttpClient client;

        public SearchClient(HttpClient client)
        {
            this.client = client;
        }

        public async Task<IEnumerable<SearchEntry>> Search(string term, CancellationToken cancellationToken = default)
        {
            var response = await client.GetFromJsonAsync<IEnumerable<SearchEntry>>($"/api/search?s={HttpUtility.UrlEncode(term)}", cancellationToken);
            if (response == null)
            {
                return Enumerable.Empty<SearchEntry>();
            }

            return response;
        }
    }
}