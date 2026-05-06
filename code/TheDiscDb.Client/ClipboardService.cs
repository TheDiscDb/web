using Microsoft.JSInterop;

namespace TheDiscDb.Client;

public interface IClipboardService
{
    ValueTask<string> ReadTextAsync(CancellationToken cancellationToken = default);
    ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class ClipboardService : IClipboardService
{
    private readonly IJSRuntime jsRuntime;

    public ClipboardService(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime;
    }

    public ValueTask<string> ReadTextAsync(CancellationToken cancellationToken = default)
    {
        return jsRuntime.InvokeAsync<string>("navigator.clipboard.readText", cancellationToken: cancellationToken);
    }

    public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        return jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", cancellationToken, text);
    }
}
