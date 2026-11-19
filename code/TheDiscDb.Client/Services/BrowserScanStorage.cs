namespace TheDiscDb.Client.Services;

using System.Text.Json;
using Microsoft.JSInterop;
using TheDiscDb.Core.DiscHash;

public sealed record LocalDiscScan(string Id, DiscScanDraft Draft);

public sealed class BrowserScanStorage(IJSRuntime js) : IAsyncDisposable
{
    internal const string ModulePath = "./Pages/Contribute/ResumeContribution.razor.js";
    private IJSObjectReference? module;

    public Task<LocalDiscScan?> GetScanAsync(string hash, CancellationToken cancellationToken = default)
        => GetAsync<LocalDiscScan>("scans", hash.ToUpperInvariant(), cancellationToken);

    public async Task SaveScanAsync(DiscScanDraft draft, CancellationToken cancellationToken = default)
        => await SetAsync("scans", draft.DiscHash.ToUpperInvariant(),
            new LocalDiscScan(Guid.NewGuid().ToString("N"), draft with { CreatedUtc = DateTimeOffset.UtcNow }), cancellationToken);

    public Task<ContributionContinuation?> GetContinuationAsync(string id, CancellationToken cancellationToken = default)
        => GetAsync<ContributionContinuation>("continuations", id, cancellationToken);

    public Task SaveContinuationAsync(string id, ContributionContinuation continuation, CancellationToken cancellationToken = default)
        => SetAsync("continuations", id, continuation, cancellationToken);

    public async Task RemoveScanAsync(string hash, string expectedId, CancellationToken cancellationToken = default)
    {
        var module = await GetModuleAsync(cancellationToken);
        await module.InvokeVoidAsync("removeScan", cancellationToken, hash.ToUpperInvariant(), expectedId);
    }

    private async Task<T?> GetAsync<T>(string store, string key, CancellationToken cancellationToken)
    {
        var module = await GetModuleAsync(cancellationToken);
        string? json = await module.InvokeAsync<string?>("get", cancellationToken, store, key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    private async Task SetAsync<T>(string store, string key, T value, CancellationToken cancellationToken)
    {
        var module = await GetModuleAsync(cancellationToken);
        await module.InvokeVoidAsync("set", cancellationToken, store, key, JsonSerializer.Serialize(value));
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken cancellationToken)
        => this.module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (this.module is not null)
            {
                await this.module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}