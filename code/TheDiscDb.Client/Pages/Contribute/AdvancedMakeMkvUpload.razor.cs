namespace TheDiscDb.Client.Pages.Contribute;

using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Inputs;

public partial class AdvancedMakeMkvUpload
{
    [Parameter]
    public string? PowershellCommand { get; set; }

    [Parameter]
    public string? BashCommand { get; set; }

    [Parameter]
    public string DriveIndex { get; set; } = "0";

    [Parameter]
    public EventCallback<string> DriveIndexChanged { get; set; }

    [Parameter]
    public IReadOnlyList<string> DriveIndices { get; set; } = [];

    [Parameter]
    public EventCallback OnCopyPowerShell { get; set; }

    [Parameter]
    public EventCallback OnCopyBash { get; set; }

    [Parameter]
    public EventCallback<UploadChangeEventArgs> OnLogValueChange { get; set; }

    [Parameter]
    public EventCallback<UploadChangeEventArgs> OnManifestValueChange { get; set; }
}
