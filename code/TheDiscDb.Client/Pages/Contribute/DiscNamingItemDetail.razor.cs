using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class DiscNamingItemDetail : CancellableComponentBase
{
    private sealed record CopyButtonState(string IconClassName, bool IsDisabled = false);

    private static readonly CopyButtonState DefaultCopyState = new("e-icons e-copy");
    private static readonly CopyButtonState CopiedCopyState = new("e-icons e-circle-check", IsDisabled: true);

    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [Parameter]
    public string? ItemId { get; set; }

    [SupplyParameterFromQuery(Name = "popup")]
    public string? Popup { get; set; }

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [Inject]
    private IClipboardService Clipboard { get; set; } = null!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = null!;

    private readonly Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_Chapters, CopyButtonState> chapterCopyStates = new();
    private readonly Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_AudioTracks, CopyButtonState> audioTrackCopyStates = new();
    private readonly Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_SubtitleTracks, CopyButtonState> subtitleTrackCopyStates = new();
    private CopyButtonState filenameCopyState = DefaultCopyState;
    private CopyButtonState titleCopyState = DefaultCopyState;

    private IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items? item;
    private bool isLoading = true;
    private string? loadError;

    private bool IsPopup => string.Equals(this.Popup, "1", StringComparison.Ordinal);
    private bool HasChapters => this.item?.Chapters.Count > 0;
    private bool HasCustomAudioTracks => this.item?.AudioTracks.Any(t => !string.IsNullOrWhiteSpace(t.Title)) == true;
    private bool HasCustomSubtitleTracks => this.item?.SubtitleTracks.Any(t => !string.IsNullOrWhiteSpace(t.Title)) == true;

    private string BackUrl
    {
        get
        {
            var url = $"/contribution/{this.ContributionId}/discs/{this.DiscId}/naming";
            return this.IsPopup ? $"{url}?popup=1" : url;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrWhiteSpace(this.ContributionId) || string.IsNullOrWhiteSpace(this.DiscId) || string.IsNullOrWhiteSpace(this.ItemId))
        {
            this.loadError = "Invalid parameters.";
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
        if (!result.IsSuccessResult() || payload?.Disc is null)
        {
            this.loadError = "Could not load disc data.";
            this.isLoading = false;
            return;
        }

        this.item = payload.Disc.Items.FirstOrDefault(i => string.Equals(i.EncodedId, this.ItemId, StringComparison.Ordinal));
        if (this.item is null)
        {
            this.loadError = "Item not found.";
            this.isLoading = false;
            return;
        }

        this.isLoading = false;
    }

    private CopyButtonState GetTitleCopyState() => this.titleCopyState;

    private CopyButtonState GetFilenameCopyState() => this.filenameCopyState;

    private CopyButtonState GetChapterCopyState(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_Chapters chapter) =>
        this.chapterCopyStates.TryGetValue(chapter, out var state) ? state : DefaultCopyState;

    private CopyButtonState GetAudioTrackCopyState(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_AudioTracks track) =>
        this.audioTrackCopyStates.TryGetValue(track, out var state) ? state : DefaultCopyState;

    private CopyButtonState GetSubtitleTrackCopyState(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_SubtitleTracks track) =>
        this.subtitleTrackCopyStates.TryGetValue(track, out var state) ? state : DefaultCopyState;

    private async Task CopyTitleToClipboard()
    {
        if (string.IsNullOrWhiteSpace(this.item?.Name))
        {
            return;
        }

        this.titleCopyState = CopiedCopyState;
        StateHasChanged();

        await this.Clipboard.WriteTextAsync(this.item.Name);
        await Task.Delay(TimeSpan.FromSeconds(2));

        this.titleCopyState = DefaultCopyState;
        StateHasChanged();
    }

    private async Task CopyFilenameToClipboard()
    {
        if (string.IsNullOrWhiteSpace(this.item?.Filename))
        {
            return;
        }

        this.filenameCopyState = CopiedCopyState;
        StateHasChanged();

        await this.Clipboard.WriteTextAsync(this.item.Filename);
        await Task.Delay(TimeSpan.FromSeconds(2));

        this.filenameCopyState = DefaultCopyState;
        StateHasChanged();
    }

    private async Task CopyChapterToClipboard(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_Chapters chapter)
    {
        if (string.IsNullOrWhiteSpace(chapter.Title))
        {
            return;
        }

        this.chapterCopyStates[chapter] = CopiedCopyState;
        StateHasChanged();

        await this.Clipboard.WriteTextAsync(chapter.Title);
        await Task.Delay(TimeSpan.FromSeconds(2));

        this.chapterCopyStates.Remove(chapter);
        StateHasChanged();
    }

    private async Task CopyAudioTrackToClipboard(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_AudioTracks track)
    {
        if (string.IsNullOrWhiteSpace(track.Title))
        {
            return;
        }

        this.audioTrackCopyStates[track] = CopiedCopyState;
        StateHasChanged();

        await this.Clipboard.WriteTextAsync(track.Title);
        await Task.Delay(TimeSpan.FromSeconds(2));

        this.audioTrackCopyStates.Remove(track);
        StateHasChanged();
    }

    private async Task CopySubtitleTrackToClipboard(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items_SubtitleTracks track)
    {
        if (string.IsNullOrWhiteSpace(track.Title))
        {
            return;
        }

        this.subtitleTrackCopyStates[track] = CopiedCopyState;
        StateHasChanged();

        await this.Clipboard.WriteTextAsync(track.Title);
        await Task.Delay(TimeSpan.FromSeconds(2));

        this.subtitleTrackCopyStates.Remove(track);
        StateHasChanged();
    }
}
