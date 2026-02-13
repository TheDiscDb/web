using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Account;

public partial class Login : ComponentBase
{
    private AuthenticationScheme[] externalLogins = [];

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    [Inject]
    public SignInManager<TheDiscDbUser> SignInManager { get; set; } = default!;

    [Inject]
    UserManager<TheDiscDbUser> UserManager { get; set; } = default!;

    [Inject]
    NavigationManager Navigation { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        externalLogins = (await SignInManager.GetExternalAuthenticationSchemesAsync()).ToArray();

        Console.WriteLine("TestContributorLogin");
        var user = await UserManager.FindByEmailAsync("web@thediscdb.com");
        if (user != null)
        {
            Console.WriteLine("User found, logging in");
            await this.SignInManager.SignInAsync(user, isPersistent: false);
            Console.WriteLine("User logged in, redirecting");
            Navigation.NavigateTo(ReturnUrl ?? "/", forceLoad: true);
        }
        else
        {
            Console.WriteLine("User not found");
        }
    }

    private async Task TestContributorLogin(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        Console.WriteLine("TestContributorLogin");
        var user = await UserManager.FindByEmailAsync("web@thediscdb.com");
        if (user != null)
        {
            Console.WriteLine("User found, logging in");
            await this.SignInManager.SignInAsync(user, isPersistent: false);
            Console.WriteLine("User logged in, redirecting");
            Navigation.NavigateTo(ReturnUrl ?? "/", forceLoad: true);
        }
        else
        {
            Console.WriteLine("User not found");
        }
    }
}
