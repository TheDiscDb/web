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

            List<(SearchEntry Document, double? Score)> results = new();
            HashSet<string> dedupe = new();

            foreach (var item in response.Value.GetResults())
            {
                if (item?.Document?.Type == null || item.Document.RelativeUrl == null)
                    continue;

                if (!IsAllowedType(item.Document.Type))
                    continue;

                if (!dedupe.Contains(item.Document.RelativeUrl))
                {
                    results.Add((item.Document, item.Score));
                    dedupe.Add(item.Document.RelativeUrl);
                }
            }

            return results
                .OrderByDescending(r => r.Score ?? 0)
                .Select(r => r.Document);
        }
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "movie", "series", "boxset"
        };

        private static bool IsAllowedType(string? type) => type != null && AllowedTypes.Contains(type);

        public async Task<IEnumerable<SearchEntry>> Suggest(string term, int limit = 5, CancellationToken cancellationToken = default)
        {
            // Fetch many more results because most will be filtered out (discs, titles, etc.)
            var searchOptions = new Azure.Search.Documents.SearchOptions
            {
                Size = Math.Max(limit * 20, 50),
                QueryType = Azure.Search.Documents.Models.SearchQueryType.Simple
            };

            var response = await this.client.SearchAsync<SearchEntry>(term, searchOptions, cancellationToken);

            List<SearchEntry> candidates = new();
            HashSet<string> dedupe = new();

            foreach (var item in response.Value.GetResults())
            {
                if (item?.Document?.RelativeUrl == null || item.Document.Type == null)
                    continue;

                if (!IsAllowedType(item.Document.Type))
                    continue;

                if (!dedupe.Contains(item.Document.RelativeUrl))
                {
                    candidates.Add(item.Document);
                    dedupe.Add(item.Document.RelativeUrl);
                }
            }

            return candidates
                .Take(limit);
        }
    }
}
