using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

namespace TheDiscDb.Client.Controls;

public partial class ReleaseDateInput : ComponentBase
{
    [Parameter]
    public string Label { get; set; } = "Release Date";

    [Parameter]
    public string Placeholder { get; set; } = "e.g. January 15, 2025 or 01-15-2025";

    [Parameter]
    public DateTimeOffset? Value { get; set; }

    [Parameter]
    public EventCallback<DateTimeOffset?> ValueChanged { get; set; }

    [Parameter]
    public bool Required { get; set; }

    private string dateText = string.Empty;
    private string? validationMessage;

    protected override void OnParametersSet()
    {
        if (Value.HasValue && string.IsNullOrEmpty(dateText))
        {
            dateText = Value.Value.ToString("MM-dd-yyyy");
        }
    }

    private void OnDateInput(ChangeEventArgs args)
    {
        var input = args.Value?.ToString() ?? string.Empty;
        dateText = input;
        validationMessage = null;

        // Only attempt Amazon format parse on input — it's an unambiguous full format
        // that users paste from product pages. General parsing is deferred to blur/submit.
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (DateTimeOffset.TryParseExact(input, "MMMM d, yyyy", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            dateText = parsedDate.ToString("MM-dd-yyyy");
        }
    }

    private async Task OnBlur(FocusEventArgs args)
    {
        await ParseAndEmit();
    }

    private async Task ParseAndEmit()
    {
        validationMessage = null;

        if (string.IsNullOrWhiteSpace(dateText))
        {
            await ValueChanged.InvokeAsync(null);
            return;
        }

        if (DateTimeOffset.TryParseExact(dateText, "MMMM d, yyyy", null, System.Globalization.DateTimeStyles.None, out var parsedDate)
            || DateTimeOffset.TryParse(dateText, out parsedDate))
        {
            dateText = parsedDate.ToString("MM-dd-yyyy");
            await ValueChanged.InvokeAsync(parsedDate);
        }
        else
        {
            validationMessage = $"'{dateText}' is not a valid date.";
            await ValueChanged.InvokeAsync(null);
        }
    }

    public bool Validate()
    {
        if (Required && !Value.HasValue)
        {
            if (string.IsNullOrWhiteSpace(dateText))
            {
                validationMessage = $"{Label} is required.";
            }
            else
            {
                validationMessage = $"'{dateText}' is not a valid date.";
            }

            return false;
        }

        if (!string.IsNullOrWhiteSpace(dateText) && !Value.HasValue)
        {
            validationMessage = $"'{dateText}' is not a valid date.";
            return false;
        }

        return true;
    }
}
