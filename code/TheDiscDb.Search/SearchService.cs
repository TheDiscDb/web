namespace TheDiscDb.Search
{
    using Azure;
    using Azure.Search.Documents;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public class SearchService : ISearchService
    {
        private readonly SearchClient client;

        public SearchService(IOptions<SearchOptions> options)
        {
            if (options?.Value?.Endpoint == null)
            {
                throw new ArgumentNullException("Search:Endpoint");
            }

            if (options?.Value?.Index == null)
            {
                throw new ArgumentNullException("Search:Index");
            }

            if (options?.Value?.ApiKey == null)
            {
                throw new ArgumentNullException("Search:ApiKey");
            }

            this.client = new SearchClient(new Uri(options.Value.Endpoint), options.Value.Index, new AzureKeyCredential(options.Value.ApiKey));
        }

        public async Task<IEnumerable<SearchEntry>> Search(string term, CancellationToken cancellationToken = default)
        {
            var response = await this.client.SearchAsync<SearchEntry>(term, cancellationToken: cancellationToken);

            List<SearchEntry> results = new();
            List<SearchEntry> subResults = new();

            HashSet<string> dedupe = new();

            foreach (var item in response.Value.GetResults())
            {
                if (item?.Document?.Type == null)
                {
                    continue;
                }

                if (item.Document.Type.Equals("movie", StringComparison.OrdinalIgnoreCase) ||
                    item.Document.Type.Equals("series", StringComparison.OrdinalIgnoreCase) ||
                    item.Document.Type.Equals("boxset", StringComparison.OrdinalIgnoreCase))
                {
                    if (item?.Document.RelativeUrl != null && !dedupe.Contains(item.Document.RelativeUrl))
                    {
                        results.Add(item.Document);
                        dedupe.Add(item.Document.RelativeUrl);
                    }
                }
                else
                {
                    subResults.Add(item.Document);
                }
            }

            //TODO: Figure out sub items
            //foreach (var subItem in subResults)
            //{
            //    var parent = results.FirstOrDefault(p => p.id.Equals($"{p.id}-{p.Type}", StringComparison.OrdinalIgnoreCase));
            //    if (parent != null)
            //    {

            //    }
            //}

            return results;
        }
    }
}
