using System.Net.Http.Json;
using System.Web;
using FluentResults;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Client;

namespace TheDiscDb.Services.Client;

public class ExternalSearchService : ApiClient, IExternalSearchService
{
    public ExternalSearchService(IHttpClientFactory httpClientFactory, NavigationManager navigation)
        : base(httpClientFactory, navigation)
    {
    }

    public async Task<Result<ExternalSearchResponse>> SearchMovies(string query, CancellationToken cancellationToken)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<ExternalSearchResponse>($"/api/contribute/externalsearch/movie?query={HttpUtility.UrlEncode(query)}", cancellationToken);
        if (response == null)
        {
            return Result.Fail("Unable to perform external search");
        }

        return response;
    }

    public async Task<Result<ExternalSearchResponse>> SearchSeries(string query, CancellationToken cancellationToken)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<ExternalSearchResponse>($"/api/contribute/externalsearch/series?query={HttpUtility.UrlEncode(query)}", cancellationToken);
        if (response == null)
        {
            return Result.Fail("Unable to perform external search");
        }

        return response;
    }
}
