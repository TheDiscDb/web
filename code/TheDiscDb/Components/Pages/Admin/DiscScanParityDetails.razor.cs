namespace TheDiscDb.Components.Pages.Admin;

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Services;
using TheDiscDb.Services.Admin;
using TheDiscDb.Web.Data;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class DiscScanParityDetails : ComponentBase
{
    [Inject]
    private IContributionDiscComparisonAdminService ComparisonAdminService { get; set; } = null!;

    [Inject]
    private IDiscScanComparisonService ComparisonService { get; set; } = null!;

    [Parameter]
    public int DiscId { get; set; }

    private IReadOnlyList<ContributionDiscComparisonListItem> History { get; set; } = [];

    private ContributionDiscComparisonListItem? Latest => History.FirstOrDefault();

    private DiscLogManifestDifferences? Differences { get; set; }

    private bool isRecompareRunning;

    private string? statusMessage;

    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        History = await ComparisonAdminService.GetDiscComparisonsAsync(DiscId);
        Differences = TryParseDifferences(Latest?.DifferencesJson);
    }

    private async Task RecompareAsync()
    {
        isRecompareRunning = true;
        statusMessage = null;
        try
        {
            var result = await ComparisonService.RecompareDiscAsync(DiscId);
            statusMessage = result == null
                ? "This disc needs both a MakeMKV log and a manifest before it can be compared."
                : "Disc re-compared.";
            await LoadAsync();
        }
        finally
        {
            isRecompareRunning = false;
        }
    }

    private static DiscLogManifestDifferences? TryParseDifferences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DiscLogManifestDifferences>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            return null;
        }
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

    private static string GetAt(IReadOnlyList<string>? values, int index)
        => values != null && index >= 0 && index < values.Count ? values[index] : string.Empty;

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");

    private static (string Text, string Url) GetParityLink() => ("Disc scan parity", "/admin/disc-scan-parity");
}
