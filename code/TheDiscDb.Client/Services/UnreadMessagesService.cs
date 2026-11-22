using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Services;

public interface IUnreadMessagesService
{
    Task<bool> HasUnreadMessagesAsync(CancellationToken cancellationToken = default);
}

public class BrowserUnreadMessagesService(IContributionClient client) : IUnreadMessagesService
{
    public async Task<bool> HasUnreadMessagesAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.HasUnreadMessages.ExecuteAsync(cancellationToken);
        if (!result.IsSuccessResult() || result.Data == null)
            throw new InvalidOperationException("Unable to load unread message status.");
        return result.Data.HasUnreadMessages;
    }
}