using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Naming;

namespace TheDiscDb.Client.Pages.Settings;

public partial class FileNameTemplates : ComponentBase
{
    [Inject]
    public GetMyFileNameTemplatesQuery MyTemplatesQuery { get; set; } = default!;

    [Inject]
    public SetFileNameTemplateMutation SetMutation { get; set; } = default!;

    [Inject]
    public DeleteFileNameTemplateMutation DeleteMutation { get; set; } = default!;

    private static readonly NamingContext PreviewContext = new()
    {
        Title = "Sample Movie",
        Year = "2024",
        FullTitle = "Sample Movie (2024)",
        Edition = "Extended",
        Format = "Blu-ray",
        Resolution = "1080p",
        TmdbId = "12345",
        ImdbId = "tt7654321",
        SeasonNumber = "01",
        EpisodeNumber = "03",
        EpisodeName = "Pilot",
        ExtraType = "Featurette",
        Part = "pt1",
        Description = "A Special Featurette"
    };

    public bool IsLoading { get; private set; } = true;

    public IReadOnlyList<TemplateEditor> Editors { get; private set; } = [];

    public IReadOnlyList<string> KnownTokens { get; } = TokenDefinitions.Accessors.Keys.ToList();

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        this.IsLoading = true;
        var overrides = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        try
        {
            var result = await this.MyTemplatesQuery.ExecuteAsync();
            if (result.IsSuccessResult() && result.Data?.MyFileNameTemplates is not null)
            {
                foreach (var t in result.Data.MyFileNameTemplates)
                {
                    overrides[t.ItemType] = t.Template;
                }
            }
        }
        catch
        {
            // Show defaults if the call fails.
        }

        this.Editors = DefaultFileNameTemplates.KnownItemTypes
            .Select(itemType =>
            {
                var defaultTemplate = DefaultFileNameTemplates.GetDefault(itemType) ?? string.Empty;
                overrides.TryGetValue(itemType, out var overrideValue);

                var editor = new TemplateEditor
                {
                    ItemType = itemType,
                    DefaultTemplate = defaultTemplate,
                    OriginalValue = overrideValue ?? string.Empty,
                    CurrentValue = overrideValue ?? string.Empty,
                    HasOverride = !string.IsNullOrEmpty(overrideValue),
                };
                UpdatePreview(editor);
                return editor;
            })
            .ToList();

        this.IsLoading = false;
    }

    private void OnTemplateInput(TemplateEditor editor, ChangeEventArgs args)
    {
        editor.CurrentValue = args.Value?.ToString() ?? string.Empty;
        editor.StatusMessage = string.Empty;
        UpdatePreview(editor);
    }

    private static void UpdatePreview(TemplateEditor editor)
    {
        var templateToPreview = string.IsNullOrWhiteSpace(editor.CurrentValue)
            ? editor.DefaultTemplate
            : editor.CurrentValue;

        var parseResult = NamingTemplate.Parse(templateToPreview);
        if (!parseResult.IsSuccess)
        {
            editor.ParseError = parseResult.Errors is { Count: > 0 }
                ? string.Join("; ", parseResult.Errors.Select(e => e.Message))
                : "Invalid template.";
            editor.Preview = string.Empty;
            editor.CanSave = false;
            return;
        }

        editor.ParseError = string.Empty;
        editor.Preview = parseResult.Template!.Format(PreviewContext);

        editor.CanSave = !string.IsNullOrWhiteSpace(editor.CurrentValue)
                        && editor.CurrentValue != editor.OriginalValue;
    }

    private async Task SaveAsync(TemplateEditor editor)
    {
        if (!editor.CanSave)
        {
            return;
        }

        editor.StatusMessage = "Saving...";
        StateHasChanged();

        var result = await this.SetMutation.ExecuteAsync(new SetFileNameTemplateInput
        {
            ItemType = editor.ItemType,
            Template = editor.CurrentValue,
        });

        if (result.IsSuccessResult() && result.Data?.SetFileNameTemplate?.UserFileNameTemplate is not null)
        {
            editor.OriginalValue = editor.CurrentValue;
            editor.HasOverride = true;
            editor.CanSave = false;
            editor.StatusMessage = "Saved.";
        }
        else
        {
            editor.StatusMessage = "Save failed.";
        }

        StateHasChanged();
    }

    private async Task ResetAsync(TemplateEditor editor)
    {
        if (!editor.HasOverride)
        {
            return;
        }

        editor.StatusMessage = "Resetting...";
        StateHasChanged();

        var result = await this.DeleteMutation.ExecuteAsync(new DeleteFileNameTemplateInput
        {
            ItemType = editor.ItemType,
        });

        if (result.IsSuccessResult())
        {
            editor.CurrentValue = string.Empty;
            editor.OriginalValue = string.Empty;
            editor.HasOverride = false;
            editor.CanSave = false;
            editor.StatusMessage = "Reset to default.";
            UpdatePreview(editor);
        }
        else
        {
            editor.StatusMessage = "Reset failed.";
        }

        StateHasChanged();
    }

    public class TemplateEditor
    {
        public string ItemType { get; set; } = string.Empty;
        public string DefaultTemplate { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string OriginalValue { get; set; } = string.Empty;
        public bool HasOverride { get; set; }
        public bool CanSave { get; set; }
        public string ParseError { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
    }
}
