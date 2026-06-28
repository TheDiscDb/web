using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.SplitButtons;
using TheDiscDb.Chapters;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Controls;

public partial class CopyChaptersButton : ComponentBase
{
    [Parameter]
    public ICollection<Chapter> Chapters { get; set; } = [];

    [Inject]
    private IClipboardService Clipboard { get; set; } = null!;

    private CopyButtonState state = CopyButtonState.Default;

    private async Task OnPrimaryClick()
    {
        var defaultFormat = ChapterFormatter.GetFormatNames().First();
        await CopyFormat(defaultFormat);
    }

    private async Task OnItemSelected(MenuEventArgs args)
    {
        await CopyFormat(args.Item.Id);
    }

    private async Task CopyFormat(string formatName)
    {
        var text = ChapterFormatter.Format(formatName, this.Chapters);
        if (text == null)
        {
            return;
        }

        await Clipboard.WriteTextAsync(text);

        state = CopyButtonState.Copied;
        StateHasChanged();

        await Task.Delay(TimeSpan.FromSeconds(2));

        state = CopyButtonState.Default;
        StateHasChanged();
    }

    private record CopyButtonState(string IconCss, bool IsDisabled = false)
    {
        public static readonly CopyButtonState Default = new("e-icons e-copy");
        public static readonly CopyButtonState Copied = new("e-icons e-circle-check", IsDisabled: true);
    }
}
