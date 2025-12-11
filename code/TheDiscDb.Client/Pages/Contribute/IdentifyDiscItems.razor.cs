using System.ComponentModel.DataAnnotations;
using MakeMkv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Notifications;
using Syncfusion.Blazor.Popups;
using Syncfusion.Blazor.SplitButtons;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client.Pages.Contribute;

public class EpisodeIdentification
{
    [Required]
    public string? Season { get; set; }
    [Required]
    public string? Episode { get; set; }
}

public class ChapterItem
{
    public int Index { get; set; }
    public string? Title { get; set; }
}

public class AudioTrackItem
{
    public int Index { get; set; }
    public string? Title { get; set; }
    public Segment? Segment { get; set; }
    public string GetDisplayName()
    {
        if (this.Segment != null)
        {
            return $"{Segment.Name} {Segment.AudioType}";
        }

        return "";
    }
}

public class ItemIdentification
{
    public required Title Title { get; set; }
    [Required]
    public required string ItemTitle { get; set; }
    public string? Description { get; set; }
    public required string Type { get; set; }
    public EpisodeIdentification? Episode { get; set; } = new EpisodeIdentification();
    public string? DatabaseId { get; set; }
    public List<ChapterItem> Chapters { get; set; } = new List<ChapterItem>();
    public List<AudioTrackItem> AudioTracks { get; set; } = new List<AudioTrackItem>();
    public bool EpisodeTitleUserEdited { get; set; } = false;
    public Dictionary<UserContributionDisc, IEnumerable<UserContributionDiscItem>> ChapterMatches { get; set; } = new Dictionary<UserContributionDisc, IEnumerable<UserContributionDiscItem>>();

    public AddItemRequest CreateAddRequest()
    {
        return new AddItemRequest
        {
            ChapterCount = Title.ChapterCount,
            Description = Description,
            Size = Title.DisplaySize!,
            Duration = Title.Length!,
            Name = ItemTitle,
            SegmentCount = Title.Segments.Count(t => t.Type != null && t.Type.Equals("Video", StringComparison.OrdinalIgnoreCase)),
            SegmentMap = Title.SegmentMap!,
            Source = Title.Playlist!,
            Type = Type,
            Season = Episode?.Season ?? null,
            Episode = Episode?.Episode ?? null
        };
    }

    public EditItemRequest CreateEditRequest()
    {
        return new EditItemRequest
        {
            ChapterCount = Title.ChapterCount,
            Description = Description,
            Size = Title.DisplaySize!,
            Duration = Title.Length!,
            Name = ItemTitle,
            SegmentCount = Title.Segments.Count(t => t.Type != null && t.Type.Equals("Video", StringComparison.OrdinalIgnoreCase)),
            SegmentMap = Title.SegmentMap!,
            Source = Title.Playlist!,
            Type = Type,
            Season = Episode != null ? Episode.Season : null,
            Episode = Episode != null ? Episode.Episode : null
        };
    }
}

[Authorize]
public partial class IdentifyDiscItems : ComponentBase
{
    [Parameter]
    public string? ContributionId { get; set; }

    [Parameter]
    public string? DiscId { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IUserContributionService Client { get; set; } = null!;

    private string? mediaType = null;
    private IQueryable<MakeMkv.Title>? filteredTitles = null;
    private IQueryable<MakeMkv.Title>? allTitles = null;
    private UserContributionDisc? disc = null;
    private readonly Dictionary<Title, ItemIdentification> identifiedTitles = new Dictionary<Title, ItemIdentification>();
    private SeriesEpisodeNames? episodeNames = null;
    private ExternalMetadata? ExternalMetadata = null;
    private UserContribution? contribution;

    // A hacky way to prevent multiple network calls at once
    private bool callInProgress = false;

    private bool commentColumnVisible = false;

    bool showEpisodeDialog = false;
    SfDialog? episodeDialog;

    bool showItemDialog = false;
    SfDialog? itemDialog;

    bool showChapterDialog = false;
    SfDialog? chapterDialog;

    bool showAudioTrackDialog = false;
    SfDialog? audioTrackDialog;

    SfToast? toast;
    string? toastContent;
    ItemIdentification? currentItem;
    
    protected override async Task OnInitializedAsync()
    {
        var response = await this.Client.GetDiscLogs(this.ContributionId!, this.DiscId!);
        if (response?.Value != null && response.IsSuccess)
        {
            this.allTitles = response.Value.Info!.Titles.AsQueryable();
            this.filteredTitles = allTitles;
            this.disc = response.Value.Disc;
            this.contribution = response.Value.Contribution;

            if (allTitles != null && disc?.Items != null)
            {
                foreach (var item in disc.Items)
                {
                    var title = this.allTitles.FirstOrDefault(t => t.SegmentMap == item.SegmentMap && t.ChapterCount == item.ChapterCount && t.DisplaySize == item.Size);
                    if (title != null)
                    {
                        var existingItem = new ItemIdentification
                        {
                            DatabaseId = item.EncodedId,
                            Title = title,
                            Type = item.Type,
                            ItemTitle = item.Name,
                            Description = item.Description
                        };

                        if (!string.IsNullOrEmpty(item.Season))
                        {
                            existingItem.Episode = new EpisodeIdentification
                            {
                                Season = item.Season,
                                Episode = item.Episode
                            };
                        }

                        InitializeChapters(item, existingItem);
                        InitializeAudioTracks(item, existingItem, title);

                        identifiedTitles[title] = existingItem;

                        if (!string.IsNullOrEmpty(title.JavaComment))
                        {
                            this.commentColumnVisible = true;
                        }
                    }
                }
            }

            if (response.Value.Contribution != null)
            {
                var contribution = response.Value.Contribution;
                if (!string.IsNullOrEmpty(contribution.MediaType))
                {
                    this.mediaType = contribution.MediaType;
                }
            }
        }

        if (!string.IsNullOrEmpty(this.mediaType) && this.mediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            var episodeResults = await Client.GetEpisodeNames(this.ContributionId!);
            if (episodeResults != null && episodeResults.IsSuccess)
            {
                this.episodeNames = episodeResults.Value;
            }
        }

        var externalMetadataResponse = await Client.GetExternalData(this.ContributionId!);
        if (externalMetadataResponse != null && externalMetadataResponse.IsSuccess)
        {
            this.ExternalMetadata = externalMetadataResponse.Value;
        }
    }

    private static void InitializeAudioTracks(UserContributionDiscItem item, ItemIdentification existingItem, Title title)
    {
        var audioSegments = title.Segments.Where(s => s.Type != null && s.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase)).ToList();
        bool hasSavedAudioTracks = item.AudioTracks != null && item.AudioTracks.Count > 0;
        int i = 1;
        foreach (var segment in audioSegments)
        {
            if (hasSavedAudioTracks)
            {
                var existingAudioTrack = item.AudioTracks!.FirstOrDefault(at => at.Index == i);
                if (existingAudioTrack != null)
                {
                    existingItem.AudioTracks.Add(new AudioTrackItem
                    {
                        Index = existingAudioTrack.Index,
                        Title = existingAudioTrack.Title,
                        Segment = segment
                    });
                }
                else
                {
                    existingItem.AudioTracks.Add(new AudioTrackItem
                    {
                        Index = i,
                        Title = "",
                        Segment = segment
                    });
                }
            }
            else
            {
                existingItem.AudioTracks.Add(new AudioTrackItem
                {
                    Index = i,
                    Title = "",
                    Segment = segment
                });
            }
            ++i;
        }
    }

    private static void InitializeChapters(UserContributionDiscItem item, ItemIdentification existingItem)
    {
        if (item.Chapters != null && item.Chapters.Count > 0)
        {
            int chapterIndex = 1;
            foreach (var chapter in item.Chapters)
            {
                existingItem.Chapters.Add(new ChapterItem
                {
                    Index = chapter.Index,
                    Title = chapter.Title
                });
                ++chapterIndex;
            }

            // Add empty chapters if needed
            for (int i = chapterIndex; i <= item.ChapterCount; i++)
            {
                existingItem.Chapters.Add(new ChapterItem
                {
                    Index = i,
                    Title = ""
                });
            }
        }
        else if (item.ChapterCount > 0)
        {
            for (int i = 0; i < item.ChapterCount; i++)
            {
                existingItem.Chapters.Add(new ChapterItem
                {
                    Index = i + 1,
                    Title = ""
                });
            }
        }
    }

    bool IsIdentified(Title title)
    {
        return identifiedTitles.ContainsKey(title);
    }

    string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }

    string GetIdentifyButtonText(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            return item.Type;
        }

        return "Identify";
    }

    string GetTitle(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            return item.ItemTitle;
        }

        return "";
    }

    string GetSeason(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            return item?.Episode?.Season ?? "";
        }

        return "";
    }

    string GetEpisode(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            return item?.Episode?.Episode ?? "";
        }

        return "";
    }

    void NameAudioTracks(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            this.currentItem = item;
            if (item.AudioTracks.Count == 0)
            {
                var audioSegments = title.Segments.Where(s => s.Type != null && s.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase));
                int i = 1;
                foreach (var segment in audioSegments)
                {
                    item.AudioTracks.Add(new AudioTrackItem
                    {
                        Index = i++,
                        Title = "",
                        Segment = segment
                    });
                }
            }
            this.showAudioTrackDialog = true;
        }
    }

    void InputChapters(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            this.currentItem = item;
            if (item.Chapters.Count == 0)
            {
                for (int i = 0; i < title.ChapterCount; i++)
                {
                    item.Chapters.Add(new ChapterItem
                    {
                        Index = i + 1,
                        Title = ""
                    });
                }
            }

            // search for chapters on other discs in the same contribution
            if (this.disc != null)
            {
                foreach (var disc in this.contribution!.Discs)
                {
                    if (disc.EncodedId != this.disc.EncodedId)
                    {
                        var matchingItems = disc.Items.Where(it => it.Chapters.Count == item.Chapters.Count);
                        if (matchingItems.Any())
                        {
                            item.ChapterMatches[disc] = matchingItems;
                        }
                    }
                }
            }

            this.showChapterDialog = true;
        }
    }

    Task EditTitle(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            this.currentItem = item;

            if (item.Type.Equals("Episode", StringComparison.OrdinalIgnoreCase))
            {
                this.showEpisodeDialog = true;
            }
            else
            {
                // Pop up dialog to ask for the title and description
                this.showItemDialog = true;
            }

        }

        return Task.CompletedTask;
    }

    async Task RemoveIdentification(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            var result = await this.Client.DeleteItemFromDisc(this.ContributionId!, this.DiscId!, item.DatabaseId!);
            if (result.IsSuccess)
            {
                identifiedTitles.Remove(title);
                this.StateHasChanged();
            }
            else
            {
                toastContent = "Error removing identified item";
                await toast!.ShowAsync();
            }
        }
    }

    private Task ItemSelected(Title title, MenuEventArgs args)
    {
        string type = args.Item.HtmlAttributes["data-type"].ToString() ?? "Unknown";

        this.currentItem = new ItemIdentification
        {
            Title = title,
            Type = type,
            ItemTitle = ""
        };

        if (type.Equals("Episode", StringComparison.OrdinalIgnoreCase))
        {
            // Pop up dialog to ask for the season and the episode number
            this.currentItem.Episode = new EpisodeIdentification();
            this.showEpisodeDialog = true;
        }
        else
        {
            if (type.Equals("MainMovie", StringComparison.OrdinalIgnoreCase) && this.ExternalMetadata != null)
            {
                this.currentItem.ItemTitle = this.ExternalMetadata.Title;
                // TODO: Should there be a year on the current item?
            }

            // Pop up dialog to ask for the title and description
            this.showItemDialog = true;
        }

        return Task.CompletedTask;
    }

    public async Task HandleValidChapterSubmit()
    {
        if (this.currentItem == null || this.callInProgress)
        {
            return;
        }

        foreach (var chapter in this.currentItem.Chapters)
        {
            if (!string.IsNullOrEmpty(chapter.Title))
            {
                this.callInProgress = true;
                var response = await this.Client.AddChapterToItem(this.ContributionId!, this.DiscId!, this.currentItem.DatabaseId!, new AddChapterRequest
                {
                    Index = chapter.Index,
                    Title = chapter.Title!
                });
                this.callInProgress = false;

                if (!response.IsSuccess)
                {
                    toastContent = "Error adding chapter";
                    await toast!.ShowAsync();
                    continue;
                }
            }
        }

        await this.chapterDialog!.HideAsync();
    }

    public async Task HandleValidAudioTrackSubmit()
    {
        if (this.currentItem == null || this.callInProgress)
        {
            return;
        }

        foreach (var audioTrack in this.currentItem.AudioTracks)
        {
            if (!string.IsNullOrEmpty(audioTrack.Title))
            {
                this.callInProgress = true;
                var response = await this.Client.AddAudioTrackToItem(this.ContributionId!, this.DiscId!, this.currentItem.DatabaseId!, new AddAudioTrackRequest
                {
                    Index = audioTrack.Index,
                    Title = audioTrack.Title!
                });
                this.callInProgress = false;

                if (!response.IsSuccess)
                {
                    toastContent = "Error naming audio track";
                    await toast!.ShowAsync();
                    continue;
                }
            }
        }

        await this.audioTrackDialog!.HideAsync();
    }

    public async Task HandleValidItemSubmit()
    {
        if (this.currentItem == null || callInProgress)
        {
            return;
        }

        bool isEdit = this.currentItem.DatabaseId != null;
        if (isEdit)
        {
            callInProgress = true;
            var updateRequest = currentItem.CreateEditRequest();

            var updateResponse = await this.Client.EditItemOnDisc(this.ContributionId!, this.DiscId!, currentItem.DatabaseId!, updateRequest);
            callInProgress = false;

            if (updateResponse.IsSuccess)
            {
                this.identifiedTitles[currentItem.Title] = currentItem;
                this.StateHasChanged();
                await this.itemDialog!.HideAsync();
                return;
            }
            else
            {
                toastContent = "Error updating identified item";
                await toast!.ShowAsync();
                return;
            }
        }

        if (callInProgress)
        {
            return;
        }

        callInProgress = true;
        var response = await this.Client.AddItemToDisc(this.ContributionId!, this.DiscId!, currentItem.CreateAddRequest());
        callInProgress = false;

        if (response.IsSuccess)
        {
            currentItem.DatabaseId = response.Value.ItemId;
            this.identifiedTitles[currentItem.Title] = currentItem;
            this.StateHasChanged();
        }
        else
        {
            toastContent = "Error adding identified item";
            await toast!.ShowAsync();
        }

        await this.itemDialog!.HideAsync();
    }

    private async Task ItemDialogCancelClicked()
    {
        if (this.currentItem == null)
        {
            return;
        }

        bool isEdit = this.currentItem.DatabaseId != null;
        if (!isEdit)
        {
            this.identifiedTitles.Remove(this.currentItem!.Title!);
        }
        await this.itemDialog!.HideAsync();
    }

    private async Task ChapterDialogCancelClicked()
    {
        if (this.currentItem == null)
        {
            return;
        }

        bool isEdit = this.currentItem.DatabaseId != null;
        if (!isEdit)
        {
            this.currentItem!.Chapters.Clear();
        }

        await this.chapterDialog!.HideAsync();
    }

    private async Task AudioTrackDialogCancelClicked()
    {
        if (this.currentItem == null)
        {
            return;
        }

        bool isEdit = this.currentItem.DatabaseId != null;
        if (!isEdit)
        {
            this.currentItem!.AudioTracks.Clear();
        }

        await this.audioTrackDialog!.HideAsync();
    }

    public async Task HandleValidEpisodeSubmit()
    {
        if (this.currentItem == null || this.callInProgress)
        {
            return;
        }

        this.callInProgress = true;
        var response = await this.Client.AddItemToDisc(this.ContributionId!, this.DiscId!, currentItem.CreateAddRequest());
        this.callInProgress = false;

        if (response.IsSuccess)
        {
            currentItem.DatabaseId = response.Value.ItemId;
            this.identifiedTitles[currentItem.Title] = currentItem;
            this.StateHasChanged();
        }
        else
        {
            toastContent = "Error adding identified episode";
            await toast!.ShowAsync();
        }

        await this.episodeDialog!.HideAsync();
    }

    private async Task EpisodeDialogCancelClicked()
    {
        this.identifiedTitles.Remove(this.currentItem!.Title!);
        this.StateHasChanged();
        await this.episodeDialog!.HideAsync();
    }

    private void SubmitIdentifications(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        this.NavigationManager.NavigateTo($"/contribution/{this.ContributionId}");
    }

    private void TrySetEpisodeTitle(ChangeEventArgs args)
    {
        if (this.episodeNames == null || 
            this.currentItem == null ||
            this.currentItem.EpisodeTitleUserEdited || // if the title has already been filled in, leave it alone
            string.IsNullOrEmpty(this.currentItem?.Episode?.Episode) || 
            string.IsNullOrEmpty(this.currentItem?.Episode?.Season))
        {
            return;
        }

        var match = this.episodeNames.TryFind(this.currentItem.Episode.Season, this.currentItem.Episode.Episode);
        if (match != null)
        {
            this.currentItem.ItemTitle = match.EpisodeName;
        }
    }

    private void EpisodeTitleKeyPress(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs args)
    {
        if (this.currentItem == null)
        {
            return;
        }

        // Reset user edited if the item becomes empty
        if (string.IsNullOrEmpty(this.currentItem.ItemTitle))
        {
            this.currentItem.EpisodeTitleUserEdited = false;
        }
        else
        {
            this.currentItem.EpisodeTitleUserEdited = true;
        }
    }

    private void CopyChapters(MenuEventArgs args)
    {
        if (this.currentItem == null)
        {
            return;
        }

        Int32.TryParse(args.Item.HtmlAttributes["data-matchIndex"].ToString(), out int matchIndex);
        Int32.TryParse(args.Item.HtmlAttributes["data-ItemIndex"].ToString(), out int itemIndex);
        var match = this.currentItem.ChapterMatches.Keys.ElementAtOrDefault(matchIndex);
        var item = this.currentItem.ChapterMatches[match!].ElementAtOrDefault(itemIndex);
        if (item != null)
        {
            for (int i = 0; i < item.Chapters.Count; i++)
            {
                if (i < this.currentItem.Chapters.Count)
                {
                    this.currentItem.Chapters[i].Title = item.Chapters.ElementAt(i).Title;
                }
            }
        }
    }
}
