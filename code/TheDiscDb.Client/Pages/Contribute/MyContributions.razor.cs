using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class MyContributions : ComponentBase
{
    [Inject]
    GetCurrentUserContributionsQuery Query { get; set; } = null!;

    public IQueryable<IGetCurrentUserContributions_MyContributions_Nodes>? Contributions { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!OperatingSystem.IsBrowser())
        {
            // prerender pass – wait for the interactive render
            return;
        }

        await LoadContributionsAsync();
    }

    private async Task LoadContributionsAsync()
    {
        var results = await Query.ExecuteAsync();
        if (results != null && results.IsSuccessResult())
        {
            this.Contributions = results.Data!.MyContributions!.Nodes!.AsQueryable();
        }
    }
}
