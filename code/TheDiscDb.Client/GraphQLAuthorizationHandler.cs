using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace TheDiscDb.Client;

public class GraphQLAuthorizationHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider tokenProvider;

    public GraphQLAuthorizationHandler(IAccessTokenProvider tokenProvider)
    {
        this.tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        var tokenResult = await tokenProvider.RequestAccessToken();
        
        if (tokenResult.TryGetToken(out var token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}