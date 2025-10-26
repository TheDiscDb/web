using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Sqids;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class ContributionDiscs : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    IUserContributionService? ContributionService { get; set; }

    private UserContribution? Contribution { get; set; }

    private IQueryable<UserContributionDisc>? Discs => Contribution?.Discs.OrderBy(d => d.Index).AsQueryable();

    protected override async Task OnInitializedAsync()
    {
        if (this.ContributionService == null)
        {
            throw new Exception("Contribution Service was not injected");
        }

        var result = await this.ContributionService.GetContribution(ContributionId!);
        if (result.IsSuccess)
        {
            this.Contribution = result.Value;
        }
    }
}
