using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TheDiscDb.Client.Controls;
using TheDiscDb.Client.Interop;
using TheDiscDb.Client.Services;
using TheDiscDb.OpticalDiscManifest.Models;
using TheDiscDb.OpticalDiscManifest.Presentation;

namespace TheDiscDb.Client.Pages.Development;

public partial class OpticalDiscManifestWorkbench : CancellableComponentBase
{
    [Inject]
    public DiscDirectoryPicker DiscDirectoryPicker { get; set; } = default!;

    [Inject]
    public BrowserOpticalDiscManifestScanner Scanner { get; set; } = default!;

    [Inject]
    public IWebAssemblyHostEnvironment HostEnvironment { get; set; } = default!;

    private bool isDevelopment;
    private bool isScanning;
    private string stage = "Preparing scan...";
    private string? error;
    private string? json;
    private ManifestGenerationResult? result;
    private IReadOnlyList<ManifestTitleSummaryRow> titleRows = [];
    private IReadOnlyList<ManifestClipSummaryRow> clipRows = [];
    private string titleFilter = string.Empty;

    private IEnumerable<ManifestTitleSummaryRow> FilteredTitleRows =>
        string.IsNullOrWhiteSpace(titleFilter)
            ? titleRows
            : titleRows.Where(row =>
                row.SourcePath.Contains(titleFilter, StringComparison.OrdinalIgnoreCase)
                || (row.SegmentClipIds?.Contains(titleFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (row.StereoscopicSummary?.Contains(titleFilter, StringComparison.OrdinalIgnoreCase) ?? false));

    protected override void OnInitialized()
    {
        isDevelopment = HostEnvironment.Environment == "Development";
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && isDevelopment)
        {
            await DiscDirectoryPicker.PreloadAsync(CancellationToken);
        }
    }

    private async Task ScanAsync()
    {
        Reset();
        try
        {
            await using var selection = await DiscDirectoryPicker.PickAsync(
                CancellationToken,
                onSelectionCommitted: () =>
                {
                    isScanning = true;
                    stage = "Enumerating disc files...";
                    StateHasChanged();
                });
            if (selection is null)
            {
                return;
            }

            result = await Scanner.ScanAsync(
                selection.Files,
                reportProgress: message =>
                {
                    stage = message;
                    StateHasChanged();
                },
                CancellationToken);
            json = Encoding.UTF8.GetString(result.Json);
            titleRows = ManifestTitleSummaryBuilder.Build(result.Manifest.Disc.Titles);
            clipRows = ManifestClipSummaryBuilder.Build(result.Manifest.Disc.Clips);
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = $"Could not generate the manifest: {ex.Message}";
        }
        finally
        {
            isScanning = false;
        }
    }

    private async Task DownloadAsync()
    {
        if (result is null || !result.Validation.IsValid)
        {
            return;
        }

        await DiscDirectoryPicker.DownloadAsync(
            "optical-disc-manifest.odm.json",
            "application/vnd.thediscdb.optical-disc-manifest+json",
            result.Json,
            CancellationToken);
    }

    private void Reset()
    {
        error = null;
        json = null;
        result = null;
        stage = "Preparing scan...";
        titleRows = [];
        clipRows = [];
        titleFilter = string.Empty;
    }

    private string FormatMemoryDelta()
    {
        if (result is null)
        {
            return "Unavailable";
        }

        long delta = result.Metrics.ManagedMemoryAfterBytes - result.Metrics.ManagedMemoryBeforeBytes;
        return $"{delta / 1024d:N1} KiB";
    }

    private static string FormatLocation(ManifestDiagnostic diagnostic)
    {
        if (diagnostic.Path is null)
        {
            return diagnostic.ByteOffset is null ? string.Empty : $"byte {diagnostic.ByteOffset}";
        }

        return diagnostic.ByteOffset is null
            ? diagnostic.Path
            : $"{diagnostic.Path} @ byte {diagnostic.ByteOffset}";
    }
}
