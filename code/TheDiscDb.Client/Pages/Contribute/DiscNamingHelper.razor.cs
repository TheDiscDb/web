using System.Globalization;
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

    private int quickGridVersion = 0;
    private bool hasCustomSort = false;

    private static readonly GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> SortSource =
        GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items>.ByAscending(i => i.Source);
    private static readonly GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> SortDescription =
        GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items>.ByAscending(i => i.Name);
    private static readonly GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> SortFilename =
        GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items>.ByAscending(i => i.Filename);
    private static readonly GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> SortDuration =
        GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items>.ByAscending(i => ParseLengthToSeconds(i.Duration));
    private static readonly GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> SortSize =
        GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items>.ByAscending(i => ParseDisplaySizeToBytes(i.Size));
    private static readonly GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> SortChapters =
        GridSort<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items>.ByAscending(i => i.ChapterCount);

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
        this.identifiedItems = OrderItemsLikeIdentifyPage(this.disc.Items, payload.Info?.Titles);

        this.isLoading = false;
    }

    // Present identified items in the same order they appear on the disc identify page,
    // which renders the parsed log titles in their natural (index) order. Each item is
    // matched to its source title and ordered by that title's index; unmatched items fall
    // back to a stable Source-based ordering after the matched ones.
    private static List<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> OrderItemsLikeIdentifyPage(
        IReadOnlyList<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> items,
        IReadOnlyList<IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles>? titles)
    {
        var identified = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Type))
            .ToList();

        if (titles is null || titles.Count == 0)
        {
            return identified
                .OrderBy(i => i.Source, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var matchedTitles = new HashSet<IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles>();
        var titleIndexByItem = new Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items, int>();

        foreach (var item in identified)
        {
            var title = ContributionDiscTitleMatcher.FindMatch(
                titles,
                matchedTitles,
                item.Source,
                item.SegmentMap,
                item.ChapterCount,
                item.Size,
                t => t.Playlist,
                t => t.SegmentMap,
                t => t.ChapterCount,
                t => t.DisplaySize);

            if (title != null)
            {
                matchedTitles.Add(title);
                titleIndexByItem[item] = title.Index;
            }
            else
            {
                titleIndexByItem[item] = int.MaxValue;
            }
        }

        return identified
            .OrderBy(i => titleIndexByItem[i])
            .ThenBy(i => i.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private async Task SortColumnAsync(ColumnBase<IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items> column)
    {
        await column.Grid.SortByColumnAsync(column);
        this.hasCustomSort = true;
    }

    private void ResetSort()
    {
        this.hasCustomSort = false;
        this.quickGridVersion++;
    }

    private static int ParseLengthToSeconds(string? length)
    {
        if (string.IsNullOrWhiteSpace(length))
        {
            return -1;
        }

        return TimeSpan.TryParse(length, CultureInfo.InvariantCulture, out var ts)
            ? (int)ts.TotalSeconds
            : -1;
    }

    private static long ParseDisplaySizeToBytes(string? displaySize)
    {
        if (string.IsNullOrWhiteSpace(displaySize))
        {
            return long.MaxValue;
        }

        string[] parts = displaySize.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double size))
        {
            return long.MaxValue;
        }

        long multiplier = parts[1].ToUpperInvariant() switch
        {
            "KB" => 1024L,
            "MB" => 1024L * 1024L,
            "GB" => 1024L * 1024L * 1024L,
            "TB" => 1024L * 1024L * 1024L * 1024L,
            _ => 1L,
        };

        return (long)(size * multiplier);
    }
}
