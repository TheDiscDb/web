using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class ManageDiscs : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = null!;

    private readonly string powershellCommandTemplate = "Invoke-WebRequest -Uri \"{0}\" -Method POST -ContentType \"text/plain\" -Body ((& '{1}' --minlength=0 --robot info disc:{2}) | Out-String)";

    public string? PowershellCommand { get; set; }

    public int DriveIndex { get; set; } = 0;

    private string GetUri() => $"{NavigationManager.BaseUri}api/contribute/{ContributionId}/addDisc";

    private string GetMakeMkvPath() => "C:\\Program Files (x86)\\MakeMKV\\makemkvcon64.exe";

    protected override Task OnInitializedAsync()
    {
        this.PowershellCommand = string.Format(powershellCommandTemplate, GetUri(), GetMakeMkvPath(), this.DriveIndex);
        return Task.CompletedTask;
    }

    private void OnDriveIndexChanged(ChangeEventArgs e)
    {
        this.PowershellCommand = string.Format(powershellCommandTemplate, GetUri(), GetMakeMkvPath(), this.DriveIndex);
    }

    private async Task CopyTextToClipboard()
    {
        if (!string.IsNullOrEmpty(this.PowershellCommand))
        {
            await JSRuntime.InvokeVoidAsync("clipboardCopy.copyText", PowershellCommand);
        }
    }
}
