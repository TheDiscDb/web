using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.DropDowns;
using Syncfusion.Blazor.Inputs;
using TheDiscDb.Client.Services;
using TheDiscDb.Naming;

namespace TheDiscDb.Client.Controls;

public partial class FilenameGenerator : ComponentBase
{
    [Parameter]
    public string? MediaType { get; set; }

    [Parameter]
    public EventCallback<ParsedTemplate?> TemplateChanged { get; set; }

    private bool IsExpanded { get; set; }
    private string? TemplateText { get; set; }
    private string? SelectedPresetName { get; set; }
    private IReadOnlyList<TemplateParseError>? ParseErrors { get; set; }
    private IReadOnlyList<FileNamePreset> Presets { get; set; } = [];

    private bool IsSeries => string.Equals(MediaType, "series", StringComparison.OrdinalIgnoreCase);

    protected override void OnParametersSet()
    {
        Presets = FileNamePresets.GetPresetsForType(MediaType);
    }

    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;

        if (!IsExpanded)
        {
            TemplateText = null;
            SelectedPresetName = null;
            ParseErrors = null;
            _ = TemplateChanged.InvokeAsync(null);
        }
    }

    private async Task OnPresetChanged(ChangeEventArgs<string, FileNamePreset> args)
    {
        if (args.ItemData is not null)
        {
            TemplateText = args.ItemData.Template;
            await ParseAndNotify();
        }
    }

    private async Task OnTemplateInput(InputEventArgs args)
    {
        TemplateText = args.Value;
        await ParseAndNotify();
    }

    private async Task ParseAndNotify()
    {
        if (string.IsNullOrWhiteSpace(TemplateText))
        {
            ParseErrors = null;
            await TemplateChanged.InvokeAsync(null);
            return;
        }

        var result = NamingTemplate.Parse(TemplateText);

        if (result.IsSuccess)
        {
            ParseErrors = null;
            await TemplateChanged.InvokeAsync(result.Template);
        }
        else
        {
            ParseErrors = result.Errors;
            await TemplateChanged.InvokeAsync(null);
        }
    }
}
