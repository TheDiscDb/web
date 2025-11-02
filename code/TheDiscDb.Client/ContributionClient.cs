using System.Net.Http.Json;
using MakeMkv;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client;

public class ContributionClient : ApiClient
{
    public ContributionClient(IHttpClientFactory httpClientFactory, NavigationManager navigation)
        : base(httpClientFactory, navigation)
    {
    }

    public async Task<CreateContributionResponse> CreateContributionAsync(CreateContributionRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
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
        var client = GetHttpClient();
        var response = await client.PostAsJsonAsync($"/api/contribute/saveDisc", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SaveDiscResponse>(cancellationToken: cancellationToken);
        if (result == null)
        {
            throw new Exception("Unable to get save disc result from server");
        }
        return result;
    }

    public async Task<DiscStatusResponse> CheckDiscUploadStatus(string discId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<DiscStatusResponse>($"/api/contribute/checkdiskuploadstatus/{discId}", cancellationToken);

        if (response == null)
        {
            throw new Exception("Unable to get disc status from server");
        }

        return response;
    }

    public async Task<DiscLogResponse> GetDiscLogsAsync(string contributionId, string discId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<DiscLogResponse>($"/api/contribute/{contributionId}/discs/{discId}/logs", cancellationToken);

        if (response == null)
        {
            throw new Exception("Unable to get disc logs from server");
        }

        return response;
    }
}

