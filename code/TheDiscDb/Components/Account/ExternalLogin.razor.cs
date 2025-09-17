
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Account;

public static class GithubClaims
{
    public const string FullName = "urn:github:name";
    public const string Url = "urn:github:url";
}

public partial class ExternalLogin : ComponentBase
{
    [Inject] public SignInManager<TheDiscDbUser> SignInManager { get; set; } = default!;
    [Inject] public UserManager<TheDiscDbUser> UserManager { get; set; } = default!;
    [Inject] public IUserStore<TheDiscDbUser> UserStore { get; set; } = default!;
    [Inject] public NavigationManager NavigationManager { get; set; } = default!;
    [Inject] public IdentityRedirectManager RedirectManager { get; set; } = default!;
    [Inject] public ILogger<ExternalLogin> Logger { get; set;} = default!;

    public const string LoginCallbackAction = "LoginCallback";

    private string? message;
    private ExternalLoginInfo? externalLoginInfo;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? RemoteError { get; set; }

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery]
    private string? Action { get; set; }

    private string? ProviderDisplayName => externalLoginInfo?.ProviderDisplayName;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        if (RemoteError is not null)
        {
            RedirectManager.RedirectToWithStatus("Account/Login", $"Error from external provider: {RemoteError}", HttpContext);
            return;
        }

        var info = await SignInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            RedirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information.", HttpContext);
            return;
        }

        externalLoginInfo = info;

        if (HttpMethods.IsGet(HttpContext.Request.Method))
        {
            if (Action == LoginCallbackAction)
            {
                await OnLoginCallbackAsync();
                return;
            }

            // We should only reach this page via the login callback, so redirect back to
            // the login page if we get here some other way.
            RedirectManager.RedirectTo("Account/Login");
        }
    }

    private async Task OnLoginCallbackAsync()
    {
        if (externalLoginInfo is null)
        {
            RedirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information.", HttpContext);
            return;
        }

        // Sign in the user with this external login provider if the user already has a login.
        var result = await SignInManager.ExternalLoginSignInAsync(
            externalLoginInfo.LoginProvider,
            externalLoginInfo.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (result.Succeeded)
        {
            Logger.LogInformation(
                "{FullName} logged in with {LoginProvider} provider.",
                externalLoginInfo.Principal.Identity?.Name,
                externalLoginInfo.LoginProvider);
            RedirectManager.RedirectTo(ReturnUrl);
            return;
        }
        else if (result.IsLockedOut)
        {
            RedirectManager.RedirectTo("Account/Lockout");
            return;
        }

        Input.Email = externalLoginInfo.Principal.FindFirstValue(ClaimTypes.Email) ?? "";
        Input.FullName = externalLoginInfo.Principal.FindFirstValue(ClaimTypes.Name)??
                         externalLoginInfo.Principal.FindFirstValue(GithubClaims.FullName) ?? "";
        Input.UserName = externalLoginInfo.Principal.FindFirstValue(ClaimTypes.Name) ?? "";
        Input.Url = externalLoginInfo.Principal.FindFirstValue(GithubClaims.Url) ?? "";
    }

    private async Task OnValidSubmitAsync()
    {
        if (externalLoginInfo is null)
        {
            RedirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information during confirmation.", HttpContext);
            return;
        }

        var emailStore = GetEmailStore();
        var user = new TheDiscDbUser();
        var claimStore = GetClaimStore();

        await UserStore.SetUserNameAsync(user, Input.UserName, CancellationToken.None);
        await emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
        await claimStore.AddClaimsAsync(user, new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Input.FullName),
            new Claim(GithubClaims.Url, Input.Url),
        }, CancellationToken.None);

        var result = await UserManager.CreateAsync(user);
        if (result.Succeeded)
        {
            result = await UserManager.AddLoginAsync(user, externalLoginInfo);
            if (result.Succeeded)
            {
                Logger.LogInformation("User created an account using {FullName} provider.", externalLoginInfo.LoginProvider);

                await SignInManager.SignInAsync(user, isPersistent: false, externalLoginInfo.LoginProvider);
                RedirectManager.RedirectTo(ReturnUrl);
            }
        }
        else
        {
            message = $"Error: {string.Join(",", result.Errors.Select(error => error.Description))}";
        }
    }

    private IUserEmailStore<TheDiscDbUser> GetEmailStore()
    {
        if (!UserManager.SupportsUserEmail)
        {
            throw new NotSupportedException("The default UI requires a user store with email support.");
        }
        return (IUserEmailStore<TheDiscDbUser>)UserStore;
    }

    private IUserClaimStore<TheDiscDbUser> GetClaimStore()
    {
        if (!UserManager.SupportsUserClaim)
        {
            throw new NotSupportedException("The default UI requires a user store with claim support.");
        }

        return (IUserClaimStore<TheDiscDbUser>)UserStore;
    }

    private sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string UserName { get; set; } = "";

        public string FullName { get; set; } = "";
        
        public string Url { get; set; } = "";
    }
}
