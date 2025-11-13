using TheDiscDb.Client;

namespace TheDiscDb;

public class ServerClipboardService : IClipboardService
{
    public ValueTask<string> ReadTextAsync()
    {
        return ValueTask.FromResult(string.Empty);
    }

    public ValueTask WriteTextAsync(string text)
    {
        return ValueTask.CompletedTask;
    }
}
