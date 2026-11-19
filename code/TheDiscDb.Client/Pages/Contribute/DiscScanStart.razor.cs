using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StrawberryShake;
using TheDiscDb.Client.Interop;
using TheDiscDb.Client.Services;
using TheDiscDb.Core.DiscHash;

namespace TheDiscDb.Client.Pages.Contribute;

/// <summary>
/// Disc-first entry point: scans into browser storage before any login is required.
/// </summary>
public partial class DiscScanStart
{
    [Parameter]
    public RenderFragment? ButtonContent { get; set; }

    [Parameter]
    public string? ButtonCssClass { get; set; }

    [Inject]
    private DiscDirectoryPicker DiscDirectoryPicker { get; set; } = default!;

    [Inject]
    private DiscScanUploader ScanUploader { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IWebAssemblyHostEnvironment HostEnvironment { get; set; } = default!;

    [Inject]
    private GetDiscDetailByContentHashQuery ContentHashQuery { get; set; } = default!;

    private bool IsDevelopmentMode => this.HostEnvironment.Environment == "Development";
    private string manualHash = string.Empty;
    private bool isScanning;
    private string? scanProgress;
    private string? error;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await this.DiscDirectoryPicker.PreloadAsync(this.CancellationToken);
        }
    }

    private async Task ScanDiscAsync()
    {
        this.error = null;
        this.scanProgress = null;
        string? hashOverride = this.IsDevelopmentMode && !string.IsNullOrWhiteSpace(this.manualHash)
            ? this.manualHash.Trim()
            : null;
        try
        {
            var result = await this.ScanUploader.ScanAndSaveDraftAsync(
                this.NavigationManager.BaseUri,
                this.MarkScanning,
                this.ReportScanProgress,
                this.CancellationToken,
                hashOverride,
                this.IsKnownDiscAsync,
                saveLocally: true);
            this.error = result.Result.Error;
            if (result.Result.Uploaded && result.DiscHash is not null)
            {
                this.NavigationManager.NavigateTo(result.Result.ExistingDisc
                    ? ContributionContinuation.MatchesUrl(result.DiscHash)
                    : ContributionContinuation.SearchUrl("movie", result.DiscHash));
            }
        }
        finally
        {
            this.isScanning = false;
            this.scanProgress = null;
        }
    }

    private async Task<bool> IsKnownDiscAsync(string discHash, CancellationToken cancellationToken)
    {
        var response = await this.ContentHashQuery.ExecuteAsync(discHash, templates: null, cancellationToken: cancellationToken);
        if (!response.IsSuccessResult())
        {
            // Fall back to a full scan when the lookup is unavailable.
            return false;
        }

        return response.Data?.MediaItems?.Nodes?
            .SelectMany(media => media.Releases)
            .SelectMany(release => release.Discs)
            .Any(disc => string.Equals(disc.ContentHash, discHash, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private void MarkScanning()
    {
        this.isScanning = true;
        this.StateHasChanged();
    }

    private void ReportScanProgress(string message)
    {
        this.scanProgress = message;
        this.InvokeAsync(this.StateHasChanged);
    }
}