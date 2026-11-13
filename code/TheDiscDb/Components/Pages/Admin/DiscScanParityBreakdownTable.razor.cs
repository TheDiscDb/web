namespace TheDiscDb.Components.Pages.Admin;

using Microsoft.AspNetCore.Components;
using TheDiscDb.Services.Admin;

public partial class DiscScanParityBreakdownTable
{
    [Parameter]
    public string Title { get; set; } = string.Empty;

    [Parameter]
    public IReadOnlyList<ContributionDiscParityBreakdown> Rows { get; set; } = [];

    private static string FormatRate(ContributionDiscParityStats stats)
        => stats.MatchRate is { } rate ? rate.ToString("P1") : "Not measured";
}
