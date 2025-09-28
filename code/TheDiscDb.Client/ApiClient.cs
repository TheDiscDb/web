using System.Net.Http.Json;
using System.Web;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Search;

namespace TheDiscDb.Client;

public class ApiClient
{
    private readonly HttpClient client;

    public ApiClient(HttpClient client)
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

    public async Task<HashResponse> HashAsync(HashRequest request, CancellationToken cancellationToken = default)
    {
        var response = await client.PostAsJsonAsync("/api/hash", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<HashResponse>(cancellationToken: cancellationToken);
        if (result == null)
        {
            throw new Exception("Unable to get hash result from server");
        }
        return result;
    }

    public async Task<CreateContributionResponse> CreateContributionAsync(CreateContributionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await client.PostAsJsonAsync("/api/contribute/create", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateContributionResponse>(cancellationToken: cancellationToken);
        if (result == null)
        {
            throw new Exception("Unable to get contribution result from server");
        }
        return result;
    }

    public async Task<SaveDiscResponse> SaveDiscAsync(SaveDiscRequest request, CancellationToken cancellationToken = default)
    {
        var response = await client.PostAsJsonAsync($"/api/contribute/saveDisc", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SaveDiscResponse>(cancellationToken: cancellationToken);
        if (result == null)
        {
            throw new Exception("Unable to get save disc result from server");
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

public class CreateContributionRequest
{
    public string DiscHash { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalProvider { get; set; } = string.Empty;
    public DateTimeOffset ReleaseDate { get; set; }
    public string Asin { get; set; } = string.Empty;
    public string Upc { get; set; } = string.Empty;
    public string FrontImageUrl { get; set; } = string.Empty;
    public string BackImageUrl { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ReleaseSlug { get; set; } = string.Empty;
}

public class CreateContributionResponse
{
    public string ContributionId { get; set; } = string.Empty;
}

public class SaveDiscRequest
{
    public int Index { get; set; }
    public string ContributionId { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class SaveDiscResponse
{
    public string DiscId { get; set; } = string.Empty;
}