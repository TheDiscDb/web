using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class MyContributions : ComponentBase
{
    [Inject]
    private IUserContributionService Client { get; set; } = null!;

    public IQueryable<UserContribution>? Contributions { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var response = await this.Client.GetUserContributions();
        if (response != null && response.IsSuccess)
        {
            this.Contributions = response.Value.AsQueryable();
        }
    }
}
