namespace TheDiscDb.Client.Pages.Contribute;

using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TheDiscDb.Client.Services;

public partial class ResumeContribution : ComponentBase
{
    [SupplyParameterFromQuery(Name = "draft")]
    public string? DraftId { get; set; }

    [Inject] private BrowserScanStorage Storage { get; set; } = default!;
    [Inject] private DiscScanHandoff Handoff { get; set; } = default!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private string? error;
    private bool isBusy;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ResumeAsync();
        }
    }

    private async Task ResumeAsync()
    {
        if (this.isBusy)
        {
            return;
        }

        this.isBusy = true;
        this.error = null;
        try
        {
            if (!Guid.TryParseExact(this.DraftId, "N", out _))
            {
                throw new InvalidOperationException("The saved contribution link is invalid. Return to scanning to continue.");
            }

            var continuation = await this.Storage.GetContinuationAsync(this.DraftId!);
            if (continuation?.IsValid != true)
            {
                throw new InvalidOperationException("The pending contribution is no longer saved in this browser. Return to scanning to continue.");
            }

            if ((await this.Authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated != true)
            {
                Login();
                return;
            }

            string destination = await this.Handoff.PrepareAsync(continuation);
            this.Navigation.NavigateTo(destination, replace: true);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Login();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or Microsoft.JSInterop.JSException or InvalidOperationException)
        {
            this.error = ex is InvalidOperationException
                ? ex.Message
                : "Your scan could not be saved to your account. It is still saved in this browser; retry to continue.";
        }
        finally
        {
            this.isBusy = false;
            StateHasChanged();
        }
    }

    private void Login()
        => this.Navigation.NavigateTo(ContributionContinuation.LoginUrl($"/contribute/resume?draft={this.DraftId}"), forceLoad: true);
}