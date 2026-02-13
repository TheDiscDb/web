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

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusFilter { get; set; }

    public IQueryable<IGetCurrentUserContributions_MyContributions_Nodes>? Contributions { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadContributionsAsync();
    }

    private async Task OnStatusFilterChanged(ChangeEventArgs e)
    {
        var selected = e.Value?.ToString();
        var uri = string.IsNullOrEmpty(selected)
            ? Navigation.GetUriWithQueryParameter("status", (string?)null)
            : Navigation.GetUriWithQueryParameter("status", selected);

        Navigation.NavigateTo(uri);

        StatusFilter = string.IsNullOrEmpty(selected) ? null : selected;
        await LoadContributionsAsync();
    }

    private async Task LoadContributionsAsync()
    {
        UserContributionFilterInput? input = null;
        if (TryGetNormalizedStatus(out var normalizedStatus))
        {
            input = new UserContributionFilterInput
            {
                Status = new UserContributionStatusOperationFilterInput
                {
                    Eq = normalizedStatus!.Value
                }
            };
        }

        var results = await Query.ExecuteAsync(input);
        if (results != null && results.IsSuccessResult())
        {
            var nodes = results.Data?.MyContributions?.Nodes ?? Array.Empty<IGetCurrentUserContributions_MyContributions_Nodes>();
            this.Contributions = nodes.AsQueryable();
        }
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
}
