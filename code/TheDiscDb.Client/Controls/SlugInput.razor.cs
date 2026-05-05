using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

namespace TheDiscDb.Client.Controls;

public enum SlugAvailability
{
    Unknown,
    Checking,
    Available,
    Taken
}

public partial class SlugInput : InputBase<string>, IDisposable
{
    /// <summary>
    /// Callback that checks whether the given slug is available.
    /// Returns true if available, false if taken.
    /// </summary>
    [Parameter]
    public Func<string, CancellationToken, Task<bool>>? CheckAvailability { get; set; }

    [Parameter]
    public int DebounceMs { get; set; } = 500;

    public SlugAvailability Status { get; private set; } = SlugAvailability.Unknown;

    private CancellationTokenSource? debounceTokenSource;
    private ValidationMessageStore? messageStore;

    protected override void OnInitialized()
    {
        this.messageStore = new ValidationMessageStore(this.EditContext);
        this.EditContext.OnValidationRequested += this.OnValidationRequested;
    }

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        this.messageStore?.Clear();
        if (this.Status == SlugAvailability.Taken)
        {
            this.messageStore?.Add(this.FieldIdentifier, "This slug is already in use for this title.");
        }
        else if (this.Status == SlugAvailability.Checking)
        {
            this.messageStore?.Add(this.FieldIdentifier, "Slug availability check is still in progress.");
        }
    }

    protected override bool TryParseValueFromString(string? value, out string result, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value ?? string.Empty;
        validationErrorMessage = null;
        return true;
    }

    private async Task HandleInput(ChangeEventArgs args)
    {
        this.CurrentValueAsString = args.Value?.ToString();
        await this.ScheduleAvailabilityCheck(this.CurrentValueAsString);
    }

    private async Task HandleBlur(FocusEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(this.CurrentValueAsString) && this.Status != SlugAvailability.Available && this.Status != SlugAvailability.Taken)
        {
            await this.CheckAvailabilityNow(this.CurrentValueAsString);
        }
    }

    /// <summary>
    /// Triggers an availability check for the current value. Call this when the slug
    /// is changed programmatically (e.g. auto-generated from a title change).
    /// </summary>
    public async Task RecheckAvailability()
    {
        if (string.IsNullOrWhiteSpace(this.CurrentValueAsString))
        {
            this.Status = SlugAvailability.Unknown;
            this.ClearValidationMessages();
            return;
        }

        await this.CheckAvailabilityNow(this.CurrentValueAsString);
    }

    private async Task ScheduleAvailabilityCheck(string? slug)
    {
        this.debounceTokenSource?.Cancel();
        this.ClearValidationMessages();

        if (string.IsNullOrWhiteSpace(slug))
        {
            this.Status = SlugAvailability.Unknown;
            StateHasChanged();
            return;
        }

        this.debounceTokenSource = new CancellationTokenSource();
        var token = this.debounceTokenSource.Token;

        try
        {
            this.Status = SlugAvailability.Checking;
            StateHasChanged();

            await Task.Delay(this.DebounceMs, token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            await this.CheckAvailabilityNow(slug, token);
        }
        catch (TaskCanceledException)
        {
            // Debounce cancelled by newer input — expected
        }
    }

    private async Task CheckAvailabilityNow(string slug, CancellationToken token = default)
    {
        if (this.CheckAvailability == null)
        {
            return;
        }

        this.Status = SlugAvailability.Checking;
        this.ClearValidationMessages();
        StateHasChanged();

        try
        {
            var isAvailable = await this.CheckAvailability(slug, token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            this.Status = isAvailable ? SlugAvailability.Available : SlugAvailability.Taken;

            if (this.Status == SlugAvailability.Taken)
            {
                this.messageStore?.Add(this.FieldIdentifier, "This slug is already in use for this title.");
            }

            this.EditContext.NotifyValidationStateChanged();
            StateHasChanged();
        }
        catch (TaskCanceledException)
        {
            // Request cancelled — expected
        }
    }

    private void ClearValidationMessages()
    {
        this.messageStore?.Clear();
        this.EditContext.NotifyValidationStateChanged();
    }

    public void Dispose()
    {
        this.debounceTokenSource?.Cancel();
        this.debounceTokenSource?.Dispose();
        if (this.EditContext != null)
        {
            this.EditContext.OnValidationRequested -= this.OnValidationRequested;
        }
    }
}
