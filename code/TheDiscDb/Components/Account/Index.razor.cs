using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Account;

[Authorize]
public partial class Index : ComponentBase
{
    private TheDiscDbUser? user;
    private string? username;

    [Inject] UserManager<TheDiscDbUser> UserManager { get; set; } = default!;
    [Inject] SignInManager<TheDiscDbUser> SignInManager { get; set; } = default!;
    [Inject] IdentityRedirectManager RedirectManager { get; set; } = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        user = await UserManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var claims = HttpContext.User.Claims.ToList();

        username = await UserManager.GetUserNameAsync(user);
    }
}