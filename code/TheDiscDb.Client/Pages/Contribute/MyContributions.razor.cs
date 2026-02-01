using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class MyContributions : ComponentBase
{
    private IEnumerable<IGetCurrentUserContributions_MyContributions_Nodes> allContributions = Enumerable.Empty<IGetCurrentUserContributions_MyContributions_Nodes>();
    private bool contributionsLoaded;

    [Inject]
    GetCurrentUserContributionsQuery Query { get; set; } = null!;

    [Inject]
    IContributionClient ContributionClient { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusFilter { get; set; }

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

    protected override void OnParametersSet()
    {
        ApplyStatusFilter();
    }

    private async Task LoadContributionsAsync()
    {
        var results = await Query.ExecuteAsync();
        if (results != null && results.IsSuccessResult())
        {
            var nodes = results.Data?.MyContributions?.Nodes ?? Array.Empty<IGetCurrentUserContributions_MyContributions_Nodes>();
            this.allContributions = nodes;
            this.contributionsLoaded = true;
            ApplyStatusFilter();
        }
    }

    private void ApplyStatusFilter()
    {
        if (!this.contributionsLoaded)
        {
            return;
        }

        if (!TryGetNormalizedStatus(out var normalizedStatus))
        {
            this.Contributions = this.allContributions.AsQueryable();
            return;
        }

        this.Contributions = this.allContributions
            .Where(c => c.Status == normalizedStatus)
            .AsQueryable();
    }

    private bool TryGetNormalizedStatus(out UserContributionStatus? normalizedStatus)
    {
        normalizedStatus = null;

        if (string.IsNullOrWhiteSpace(this.StatusFilter))
        {
            return false;
        }

        if (!Enum.TryParse(this.StatusFilter.Trim(), ignoreCase: true, out UserContributionStatus parsedStatus))
        {
            return false;
        }

        normalizedStatus = parsedStatus;
        return true;
    }

    private async Task SetStatus(IGetCurrentUserContributions_MyContributions_Nodes item, Client.Contributions.UserContributionStatus status)
    {
        var result = await this.ContributionClient.UpdateContribution.ExecuteAsync(new UpdateContributionInput
        {
            ContributionId = item.EncodedId,
            Status = status
        });

        if (result != null && result.IsSuccessResult())
        {
            ApplyStatusFilter();
        }
    }
}
