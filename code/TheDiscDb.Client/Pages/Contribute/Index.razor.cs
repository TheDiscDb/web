using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class Index : ComponentBase
{
    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    AuthenticationStateProvider? AuthProvider { get; set; }

    public IQueryable<IMyPendingContributions_MyContributions_Nodes>? PendingContributions { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (this.AuthProvider != null)
        {
            var state = await this.AuthProvider.GetAuthenticationStateAsync();
            if (state?.User?.Identity != null && state.User.Identity.IsAuthenticated)
            {
                try
                {
                    var result = await this.ContributionClient.MyPendingContributions.ExecuteAsync();
                    if (result != null && result.IsSuccessResult())
                    {
                        var pendingContributions = result.Data!.MyContributions!.Nodes!;
                        if (pendingContributions != null && pendingContributions.Any())
                        {
                            this.PendingContributions = pendingContributions.AsQueryable();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading pending contributions: {ex}");
                }
            }
        }
    }
}
