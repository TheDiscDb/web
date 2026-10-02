namespace TheDiscDb.Components.Pages.Admin;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Services;
using TheDiscDb.Services.Admin;
using TheDiscDb.Web.Data;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class DiscScanParity : ComponentBase
{
    [Inject]
    private IContributionDiscComparisonAdminService ComparisonAdminService { get; set; } = null!;

    [Inject]
    private IDiscScanComparisonService ComparisonService { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "producerVersion")]
    public string? ProducerVersion { get; set; }

    private ContributionDiscParityDashboard? dashboard;
    private string? selectedProducerVersion;
    private bool isRecompareAllRunning;
    private string? statusMessage;

    protected override async Task OnParametersSetAsync()
    {
        selectedProducerVersion = ProducerVersion;
        dashboard = await ComparisonAdminService.GetDashboardAsync(ProducerVersion);
    }

    private async Task ApplyFilterAsync()
    {
        string? version = string.IsNullOrWhiteSpace(selectedProducerVersion) ? null : selectedProducerVersion;
        string url = string.IsNullOrEmpty(version)
            ? "/admin/disc-scan-parity"
            : $"/admin/disc-scan-parity?producerVersion={Uri.EscapeDataString(version)}";
        Navigation.NavigateTo(url, replace: true);
        dashboard = await ComparisonAdminService.GetDashboardAsync(version);
    }

    private async Task RecompareAllAsync()
    {
        isRecompareAllRunning = true;
        statusMessage = null;
        try
        {
            int count = await ComparisonService.RecompareAllDiscsAsync();
            statusMessage = $"Re-compared {count} discs.";
            dashboard = await ComparisonAdminService.GetDashboardAsync(ProducerVersion);
        }
        finally
        {
            isRecompareAllRunning = false;
        }
    }

    private static string FormatRate(ContributionDiscParityStats stats)
        => stats.Total == 0 ? "0.0%" : stats.MatchRate.ToString("P1");

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
}
