using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Controls;

public partial class CopyButton : ComponentBase
{
    private sealed record CopyState(string Text, string IconClassName, bool IsDisabled = false);

    private static readonly CopyState DefaultState = new("Copy", "e-icons e-copy");
    private static readonly CopyState CopiedState = new("Copied", "e-icons e-circle-check", IsDisabled: true);

    private CopyState state = DefaultState;

    [Parameter]
    public string? Text { get; set; }

    [Parameter]
    public string Title { get; set; } = "Copy to clipboard";

    [Parameter]
    public string CssClass { get; set; } = "copy-button";

    [Parameter]
    public bool ShowLabel { get; set; }

    [Inject]
    private IClipboardService Clipboard { get; set; } = null!;

    private async Task CopyToClipboard()
    {
        if (string.IsNullOrEmpty(this.Text))
        {
            return;
        }

        var previous = this.state;
        this.state = CopiedState;
        StateHasChanged();

        try
        {
            await Clipboard.WriteTextAsync(this.Text);
        }
        catch
        {
            this.state = previous;
            StateHasChanged();
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
        this.state = previous;
        StateHasChanged();
    }
}
