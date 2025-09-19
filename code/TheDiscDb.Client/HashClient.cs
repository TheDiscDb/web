namespace TheDiscDb.Client;

using System;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Core.DiscHash;

public class HashClient
{
    private readonly HttpClient client;
    public HashClient(HttpClient client)
    {
        this.client = client;
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
}

public class HashRequest
{
    public List<FileHashInfo> Files { get; set; } = new List<FileHashInfo>();
}

public class HashResponse
{
    public string Hash { get; set; } = string.Empty;
}