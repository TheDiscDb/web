using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.JSInterop;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class DiscNamingHelper : CancellableComponentBase
{
    private sealed record CopyButtonState(string IconClassName, bool IsDisabled = false);

    private static readonly CopyButtonState DefaultCopyState = new("e-icons e-copy");
    private static readonly CopyButtonState CopiedCopyState = new("e-icons e-circle-check", IsDisabled: true);

    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [SupplyParameterFromQuery(Name = "popup")]
    public string? Popup { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    private IClipboardService Clipboard { get; set; } = null!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = null!;

    private readonly Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items, CopyButtonState> descriptionCopyStates = new();
    private readonly Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items, CopyButtonState> filenameCopyStates = new();
    private CopyButtonState titleCopyState = DefaultCopyState;

    private IGetDiscLogs_DiscLogs_DiscLogs_Contribution? contribution;
    private IGetDiscLogs_DiscLogs_DiscLogs_Disc? disc;
    private List<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> identifiedItems = [];
    private bool isLoading = true;
    private string? loadError;

    private bool IsPopup => string.Equals(this.Popup, "1", StringComparison.Ordinal);

    private IQueryable<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> IdentifiedItems => this.identifiedItems.AsQueryable();

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrWhiteSpace(this.ContributionId) || string.IsNullOrWhiteSpace(this.DiscId))
        {
            this.loadError = "Invalid contribution or disc id.";
            this.isLoading = false;
            return;
        }

        var input = new DiscLogsInput
        {
            ContributionId = this.ContributionId,
            DiscId = this.DiscId,
        };

        var result = await this.ContributionClient.GetDiscLogs.ExecuteAsync(input, this.CancellationToken);
        var payload = result.Data?.DiscLogs?.DiscLogs;
        if (!result.IsSuccessResult() || payload?.Disc is null || payload.Contribution is null)
        {
            this.loadError = "Could not load this disc's identified items.";
            this.isLoading = false;
            return;
        }

        this.disc = payload.Disc;
        this.contribution = payload.Contribution;
        this.identifiedItems = this.disc.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Type))
            .OrderBy(i => i.Source)
            .ToList();

        this.isLoading = false;
    }

    private CopyButtonState GetTitleCopyState() => this.titleCopyState;

    private CopyButtonState GetDescriptionCopyState(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item)
    {
        return this.descriptionCopyStates.TryGetValue(item, out var state) ? state : DefaultCopyState;
    }

    private CopyButtonState GetFileNameCopyState(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item)
    {
        return this.filenameCopyStates.TryGetValue(item, out var state) ? state : DefaultCopyState;
    }

    private async Task CopyTitleToClipboard()
    {
        var title = this.contribution is null ? string.Empty : $"{this.contribution.Title} ({this.contribution.Year})";
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        this.titleCopyState = CopiedCopyState;
        StateHasChanged();

        await this.Clipboard.WriteTextAsync(title);
        await Task.Delay(TimeSpan.FromSeconds(2));

        this.titleCopyState = DefaultCopyState;
        StateHasChanged();
    }

    private async Task CopyDescriptionToClipboard(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        this.descriptionCopyStates[item] = CopiedCopyState;
        StateHasChanged();

        await this.Clipboard.WriteTextAsync(item.Name);
        await Task.Delay(TimeSpan.FromSeconds(2));

        this.descriptionCopyStates.Remove(item);
        StateHasChanged();
    }

    private async Task CopyFileNameToClipboard(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item)
    {
        if (string.IsNullOrWhiteSpace(item.Filename))
        {
            return;
        }

        this.filenameCopyStates[item] = CopiedCopyState;
        StateHasChanged();

        await this.Clipboard.WriteTextAsync(item.Filename);
        await Task.Delay(TimeSpan.FromSeconds(2));

        this.filenameCopyStates.Remove(item);
        StateHasChanged();
    }

    private async Task CloseWindowAsync()
    {
        await this.JSRuntime.InvokeVoidAsync("window.close");
    }

    private string GetItemDetailUrl(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item)
    {
        var url = $"/contribution/{this.ContributionId}/discs/{this.DiscId}/naming/{item.EncodedId}";
        return this.IsPopup ? $"{url}?popup=1" : url;
    }

    private static bool HasChapters(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item) =>
        item.Chapters.Count > 0;

    private static bool HasCustomAudioTracks(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item) =>
        item.AudioTracks.Any(t => !string.IsNullOrWhiteSpace(t.Title));
}
