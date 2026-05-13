using Microsoft.JSInterop;
using TheDiscDb.Client;

namespace TheDiscDb;

public class ServerClipboardService : IClipboardService
{
    private readonly IJSRuntime jsRuntime;

    public ServerClipboardService(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime;
    }

    public ValueTask<string> ReadTextAsync(CancellationToken cancellationToken = default)
    {
        return jsRuntime.InvokeAsync<string>("navigator.clipboard.readText", cancellationToken);
    }

    public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        return jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", cancellationToken, text);
    }
}
