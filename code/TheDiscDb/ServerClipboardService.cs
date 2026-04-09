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

    public ValueTask<string> ReadTextAsync()
    {
        return jsRuntime.InvokeAsync<string>("navigator.clipboard.readText");
    }

    public ValueTask WriteTextAsync(string text)
    {
        return jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
    }
}
