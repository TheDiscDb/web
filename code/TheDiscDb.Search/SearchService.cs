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

        private const string TypeFilter = "Type eq 'Movie' or Type eq 'Series' or Type eq 'Boxset'";

        public async Task<IEnumerable<SearchEntry>> Search(string term, int? limit = null, CancellationToken cancellationToken = default)
        {
            var searchOptions = new Azure.Search.Documents.SearchOptions
            {
                Filter = TypeFilter,
                QueryType = Azure.Search.Documents.Models.SearchQueryType.Simple
            };

            if (limit.HasValue)
            {
                searchOptions.Size = Math.Max(limit.Value * 5, 25);
            }

            var response = await this.client.SearchAsync<SearchEntry>(term, searchOptions, cancellationToken);

            List<(SearchEntry Document, double? Score)> results = new();
            HashSet<string> dedupe = new(StringComparer.OrdinalIgnoreCase);

            foreach (var item in response.Value.GetResults())
            {
                if (item?.Document?.Type == null || item.Document.RelativeUrl == null)
                    continue;

                if (!dedupe.Contains(item.Document.RelativeUrl))
                {
                    results.Add((item.Document, item.Score));
                    dedupe.Add(item.Document.RelativeUrl);
                }

                if (limit.HasValue && results.Count >= limit.Value)
                    break;
            }

            return results
                .OrderByDescending(r => r.Score ?? 0)
                .Select(r => r.Document);
        }
    }
}
