using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace TheDiscDb.Client.Controls;

public partial class AsinInput : InputBase<string?>
{
    private readonly string inputId = $"asin-{Guid.NewGuid():N}";
    private readonly string unavailableInputId = $"asin-unavailable-{Guid.NewGuid():N}";
    private string? currentValue;
    private bool isUnavailable;

    [Parameter]
    public bool IsUnavailable { get; set; }

    [Parameter]
    public EventCallback<bool> IsUnavailableChanged { get; set; }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        this.currentValue = this.Value;
        this.isUnavailable = this.IsUnavailable;
    }

    protected override bool TryParseValueFromString(
        string? value,
        out string? result,
        [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }

    private async Task HandleInput(ChangeEventArgs args)
    {
        if (!this.isUnavailable)
        {
            this.currentValue = args.Value?.ToString();
            await this.ValueChanged.InvokeAsync(this.currentValue);
        }
    }

    private void HandleBlur()
    {
        this.EditContext.NotifyFieldChanged(this.FieldIdentifier);
    }

    private async Task HandleUnavailableChanged(ChangeEventArgs args)
    {
        var isUnavailable = args.Value is true;
        this.isUnavailable = isUnavailable;
        await this.IsUnavailableChanged.InvokeAsync(isUnavailable);

        if (isUnavailable)
        {
            this.EditContext.NotifyFieldChanged(this.FieldIdentifier);
        }
    }
}
