namespace TheDiscDb.Client.Services;

using System.Text.Json;
using Microsoft.JSInterop;

public sealed class ContributionDraftStorage : IAsyncDisposable
{
    private const string KeyPrefix = "thediscdb:contribution-draft:";
    private readonly IJSRuntime js;
    private IJSObjectReference? module;

    public ContributionDraftStorage(IJSRuntime js)
    {
        this.js = js;
    }

    public async Task<T?> GetAsync<T>(string discHash, CancellationToken cancellationToken = default)
    {
        var module = await GetModuleAsync(cancellationToken);
        string? json = await module.InvokeAsync<string?>("get", cancellationToken, GetKey(discHash));
        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json);
    }

    public async Task SetAsync<T>(string discHash, T value, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(value);
        var module = await GetModuleAsync(cancellationToken);
        await module.InvokeVoidAsync("set", cancellationToken, GetKey(discHash), json);
    }

    public async Task RemoveAsync(string discHash, CancellationToken cancellationToken = default)
    {
        var module = await GetModuleAsync(cancellationToken);
        await module.InvokeVoidAsync("remove", cancellationToken, GetKey(discHash));
    }

    public async ValueTask DisposeAsync()
    {
        if (this.module is not null)
        {
            await this.module.DisposeAsync();
        }
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
    {
        this.module ??= await this.js.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "/contribution-draft-storage.js");
        return this.module;
    }

    private static string GetKey(string discHash)
        => $"{KeyPrefix}{discHash.ToLowerInvariant()}";
}
