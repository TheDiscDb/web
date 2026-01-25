using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using Microsoft.AspNetCore.Components.Authorization;

namespace TheDiscDb.Client.Pages.Contribute;

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
            if (state != null)
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
        }
    }
}
