using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TheDiscDb.Client.Interop;
using TheDiscDb.Client.Services;
using TheDiscDb.Core.DiscHash;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class ScanDisc
{
    [Inject]
    private DiscDirectoryPicker DiscDirectoryPicker { get; set; } = default!;

    [Inject]
    private DiscScanUploader ScanUploader { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IWebAssemblyHostEnvironment HostEnvironment { get; set; } = default!;

    private bool IsDevelopmentMode => HostEnvironment.Environment == "Development";
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
        string? hashOverride = IsDevelopmentMode && !string.IsNullOrWhiteSpace(manualHash)
            ? manualHash.Trim()
            : null;
        try
        {
            var result = await this.ScanUploader.ScanAndSaveDraftAsync(
                this.NavigationManager.BaseUri,
                MarkScanning,
                ReportScanProgress,
                this.CancellationToken,
                hashOverride);
            this.error = result.Result.Error;
            if (result.Result.Uploaded && result.DiscHash is not null)
            {
                this.NavigationManager.NavigateTo($"/contribute/disc/{DiscScanDraft.EncodeHashForRoute(result.DiscHash)}");
            }
        }
        finally
        {
            this.isScanning = false;
            this.scanProgress = null;
        }
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
