using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Services;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class MyContributions : ComponentBase
{
    [Inject]
    private IUserContributionService Client { get; set; } = null!;

    [Inject]
    GetCurrentUserContributionsQuery Query { get; set; } = null!;

    public IQueryable<IGetCurrentUserContributions_MyContributions_Nodes>? Contributions { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var results = await Query.ExecuteAsync();
        if (results != null && results.IsSuccessResult())
        {
            this.Contributions = results.Data!.MyContributions!.Nodes!.AsQueryable();
        }
        //var response = await this.Client.GetUserContributions();
        //if (response != null && response.IsSuccess)
        //{
        //    this.Contributions = response.Value.AsQueryable();
        //}
    }
}
