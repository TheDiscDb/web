using Microsoft.AspNetCore.Components;
using TheDiscDb.Client.Services;
using TheDiscDb.Core.DiscHash;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class DiscHashLookup
{
    [Parameter] public string? DiscHash { get; set; }
    [SupplyParameterFromQuery(Name = "boxsetId")] public string? BoxsetId { get; set; }
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    private string? error;

    protected override void OnParametersSet()
    {
        if (!string.IsNullOrWhiteSpace(this.DiscHash) && DiscScanDraft.TryDecodeHashFromRoute(this.DiscHash, out string hash))
        {
            this.Navigation.NavigateTo(ContributionContinuation.SearchUrl("movie", hash, this.BoxsetId), replace: true);
        }
        else
        {
            this.error = "The disc hash URL is invalid.";
        }
    }
}