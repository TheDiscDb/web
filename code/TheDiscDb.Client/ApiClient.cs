using System.Net.Http.Json;
using System.Web;
using MakeMkv;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Search;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client;

public class ApiClient
{
    protected readonly IHttpClientFactory httpClientFactory;
    protected readonly NavigationManager navigation;

    public ApiClient(IHttpClientFactory httpClientFactory, NavigationManager navigation)
    {
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    protected HttpClient GetHttpClient()
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(navigation.BaseUri);
        return client;
    }

    public async Task<IEnumerable<SearchEntry>> Search(string term, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<IEnumerable<SearchEntry>>($"/api/search?s={HttpUtility.UrlEncode(term)}", cancellationToken);
        if (response == null)
        {
            return Enumerable.Empty<SearchEntry>();
        }

        return response;
    }

    public async Task<HashResponse> HashAsync(HashRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PostAsJsonAsync("/api/hash", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<HashResponse>(cancellationToken: cancellationToken);
        if (result == null)
        {
            throw new Exception("Unable to get hash result from server");
        }
        return result;
    }
}

public class HashRequest
{
    public List<FileHashInfo> Files { get; set; } = new List<FileHashInfo>();
}

public class HashResponse
{
    public string? Hash { get; set; }
}