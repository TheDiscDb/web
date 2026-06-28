using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components;

/// <summary>
/// Base component for pages that need the currently-authenticated user. Centralises
/// the <see cref="AuthenticationStateProvider"/> + <see cref="UserManager{T}"/> wiring
/// and the user-id lookup that was previously duplicated across the edit/review pages.
/// </summary>
public abstract class AuthenticatedComponentBase : ComponentBase
{
    [Inject]
    protected UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    protected AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

    /// <summary>
    /// Resolves the id of the currently-authenticated user, or <c>null</c> when no
    /// user is signed in.
    /// </summary>
    protected async Task<string?> GetCurrentUserIdAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        return UserManager.GetUserId(authState.User);
    }
}
