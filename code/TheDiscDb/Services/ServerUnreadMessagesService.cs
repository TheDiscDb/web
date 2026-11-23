using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services;

public class ServerUnreadMessagesService(
    IDbContextFactory<SqlServerDataContext> dbFactory,
    AuthenticationStateProvider authentication) : IUnreadMessagesService
{
    public async Task<bool> HasUnreadMessagesAsync(CancellationToken cancellationToken = default)
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        await using var database = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await MessageAccess.Inbox(database.UserMessages, user).AnyAsync(e => e.IsUnread, cancellationToken);
    }
}