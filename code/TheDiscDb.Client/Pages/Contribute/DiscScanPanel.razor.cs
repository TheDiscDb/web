namespace TheDiscDb.Client.Pages.Contribute;

using Microsoft.AspNetCore.Components;

public partial class DiscScanPanel
{
    [Parameter]
    public bool IsScanning { get; set; }

    [Parameter]
    public string? ScanProgress { get; set; }

    [Parameter]
    public IReadOnlyList<string> ScanWarnings { get; set; } = [];

    [Parameter]
    public bool ScanUploadSucceeded { get; set; }

    [Parameter]
    public bool IsDefaultMode { get; set; }

    [Parameter]
    public string ButtonText { get; set; } = "Select Disc Root Folder";

    [Parameter]
    public EventCallback OnScanDisc { get; set; }
}
