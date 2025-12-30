using System.ComponentModel.DataAnnotations;
using Fantastic.TheMovieDb.Models;
using MakeMkv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using Syncfusion.Blazor.Notifications;
using Syncfusion.Blazor.Popups;
using Syncfusion.Blazor.SplitButtons;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Client.Pages.Contribute;

public class SeriesEpisodeNames
{
    public string SeriesTitle { get; set; } = string.Empty;
    public string SeriesYear { get; set; } = string.Empty;
    public ICollection<SeriesEpisodeNameEntry> Episodes { get; set; } = new List<SeriesEpisodeNameEntry>();

    public SeriesEpisodeNameEntry? TryFind(string season, string episode)
    {
        return Episodes.FirstOrDefault(e =>
            string.Equals(e.SeasonNumber, season, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.EpisodeNumber, episode, StringComparison.OrdinalIgnoreCase));
    }
}

public class SeriesEpisodeNameEntry
{
    public string SeasonNumber { get; set; } = string.Empty;
    public string EpisodeNumber { get; set; } = string.Empty;
    public string EpisodeName { get; set; } = string.Empty;
}

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
    public IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles_Segments? Segment { get; set; }
    public string GetDisplayName()
    {
        if (this.Segment != null)
        {
            return $"{Segment.Name} {Segment.AudioType}";
        }

        return "";
    }
}

public class AddItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int ChapterCount { get; set; } = 0;
    public int SegmentCount { get; set; } = 0;
    public string SegmentMap { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    public string? Season { get; set; } = string.Empty;
    public string? Episode { get; set; } = string.Empty;
}

public class EditItemRequest : AddItemRequest
{
}

public class ItemIdentification
{
    public required IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles Title { get; set; }
    [Required]
    public required string ItemTitle { get; set; }
    public string? Description { get; set; }
    public required string Type { get; set; }
    public EpisodeIdentification? Episode { get; set; } = new EpisodeIdentification();
    public string? DatabaseId { get; set; }
    public List<ChapterItem> Chapters { get; set; } = new List<ChapterItem>();
    public List<AudioTrackItem> AudioTracks { get; set; } = new List<AudioTrackItem>();
    public bool EpisodeTitleUserEdited { get; set; } = false;
    public Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Contribution_Discs, IEnumerable<IGetDiscLogs_DiscLogs_DiscLogs_Contribution_Discs_Items>> ChapterMatches { get; set; } = new Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Contribution_Discs, IEnumerable<IGetDiscLogs_DiscLogs_DiscLogs_Contribution_Discs_Items>>();

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
    public IContributionClient ContributionClient { get; set; } = default!;

    private string? mediaType = null;
    private IQueryable<IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles>? filteredTitles = null;
    private IQueryable<IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles>? allTitles = null;
    private IGetDiscLogs_DiscLogs_DiscLogs_Disc? disc = null;
    private readonly Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles, ItemIdentification> identifiedTitles = new Dictionary<IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles, ItemIdentification>();
    //private SeriesEpisodeNames? episodeNames = null;
#pragma warning disable IDE0044 // Add readonly modifier
    private IGetEpisodeNames_EpisodeNames? episodeNames;
#pragma warning restore IDE0044 // Add readonly modifier
    private IGetExternalDataForContribution_ExternalDataForContribution_ExternalMetadata? ExternalMetadata = null;
    private IGetDiscLogs_DiscLogs_DiscLogs_Contribution? contribution;
    private bool IsDoneButtonDisabled => identifiedTitles == null || identifiedTitles.Count == 0;

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
        var input = new DiscLogsInput
        {
            ContributionId = this.ContributionId!,
            DiscId = this.DiscId!
        };
        var response = await this.ContributionClient.GetDiscLogs.ExecuteAsync(input);
        if (response?.Data != null && response.IsSuccessResult())
        {
            this.allTitles = response.Data.DiscLogs.DiscLogs!.Info!.Titles.AsQueryable();
            this.filteredTitles = allTitles;
            this.disc = response.Data.DiscLogs.DiscLogs.Disc;
            this.contribution = response.Data.DiscLogs.DiscLogs.Contribution;

            if (allTitles != null && disc?.Items != null)
            {
                foreach (IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item in disc.Items)
                {
                    IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles? title = this.allTitles.FirstOrDefault(t => t.SegmentMap == item.SegmentMap && t.ChapterCount == item.ChapterCount && t.DisplaySize == item.Size);
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

            if (response.Data.DiscLogs.DiscLogs.Contribution != null)
            {
                var contribution = response.Data.DiscLogs.DiscLogs.Contribution;
                if (!string.IsNullOrEmpty(contribution.MediaType))
                {
                    this.mediaType = contribution.MediaType;
                }
            }
        }

        if (!string.IsNullOrEmpty(this.mediaType) && this.mediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            var episodeResults = await ContributionClient.GetEpisodeNames.ExecuteAsync(new EpisodeNamesInput
            {
                ContributionId = this.ContributionId!
            });
            if (episodeResults?.Data != null && episodeResults.IsSuccessResult())
            {
                this.episodeNames = episodeResults.Data.EpisodeNames;
            }
        }

        var externalMetadataResponse = await ContributionClient.GetExternalDataForContribution.ExecuteAsync(new ExternalDataForContributionInput
        {
            ContributionId = this.ContributionId!
        });

        if (externalMetadataResponse?.Data != null && externalMetadataResponse.IsSuccessResult())
        {
            this.ExternalMetadata = externalMetadataResponse.Data.ExternalDataForContribution.ExternalMetadata;
        }
        else if (externalMetadataResponse?.Errors != null)
        {
            foreach (var error in externalMetadataResponse.Errors)
            {
                toastContent = "Error retrieving external metadata: " + error.Message;
                await toast!.ShowAsync();
            }
        }
    }

    private static void InitializeAudioTracks(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item, ItemIdentification existingItem, IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        var audioSegments = title.Segments.Where(s => s.Type != null && s.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase)).ToList();
        bool hasSavedAudioTracks = item.AudioTracks != null && item.AudioTracks.Count > 0;
        int i = 1;
        foreach (IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles_Segments? segment in audioSegments)
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

    private static void InitializeChapters(IGetDiscLogs_DiscLogs_DiscLogs_Disc_Items item, ItemIdentification existingItem)
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

    bool IsIdentified(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
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

    string GetIdentifyButtonText(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            return item.Type;
        }

        return "Identify";
    }

    string GetTitle(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            return item.ItemTitle;
        }

        return "";
    }

    string GetSeason(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            return item?.Episode?.Season ?? "";
        }

        return "";
    }

    string GetEpisode(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            return item?.Episode?.Episode ?? "";
        }

        return "";
    }

    void NameAudioTracks(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
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

    string GetChapterIconCss(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        string css = "e-icons e-changes-track";
        
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            if (item.Chapters != null && item.Chapters.Any(c => !string.IsNullOrEmpty(c.Title)))
            {
                return css + " iconIndicator";
            }
        }

        return css;
    }

    string GetChapterButtonToolTip(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            if (item.Chapters != null && item.Chapters.Any(c => !string.IsNullOrEmpty(c.Title)))
            {
                return "Edit Chapter Labels";
            }
        }

        return "Label Chapters";
    }

    bool HasAudioTracks(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title) => identifiedTitles.TryGetValue(title, out var item) && item.AudioTracks != null && item.AudioTracks.Any(c => !string.IsNullOrEmpty(c.Title));

    string GetAudioIconCss(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        string css = "e-icons e-audio";

        if (HasAudioTracks(title))
        {
            return css + " iconIndicator";
        }

        return css;
    }

    string GetAudioButtonToolTip(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        if (HasAudioTracks(title))
        {
            return "Edit Audio Track Labels";
        }

        return "Label Audio Tracks";
    }

    void InputChapters(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
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
                foreach (IGetDiscLogs_DiscLogs_DiscLogs_Contribution_Discs disc in this.contribution!.Discs)
                {
                    if (disc.EncodedId != this.disc.EncodedId)
                    {
                        IEnumerable<IGetDiscLogs_DiscLogs_DiscLogs_Contribution_Discs_Items> matchingItems = disc.Items.Where(it => it.Chapters.Count == item.Chapters.Count);
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

    Task EditTitle(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
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

    async Task RemoveIdentification(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            var result = await this.ContributionClient.DeleteItemFromDisc.ExecuteAsync(new DeleteItemFromDiscInput
            {
                ContributionId = this.ContributionId!,
                DiscId = this.DiscId!,
                ItemId = item.DatabaseId!
            });

            if (result.IsSuccessResult())
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

    private Task ItemSelected(IGetDiscLogs_DiscLogs_DiscLogs_Info_Titles title, MenuEventArgs args)
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
                var response = await this.ContributionClient.AddChapterToItem.ExecuteAsync(new AddChapterToItemInput
                {
                    ContributionId = this.ContributionId!,
                    DiscId = this.DiscId!,
                    ItemId = this.currentItem.DatabaseId!,
                    ChapterIndex = chapter.Index,
                    ChapterName = chapter.Title!
                });
                this.callInProgress = false;

                if (!response.IsSuccessResult())
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
                var response = await this.ContributionClient.AddAudioTrackToItem.ExecuteAsync(new AddAudioTrackToItemInput
                {
                    ContributionId = this.ContributionId!,
                    DiscId = this.DiscId!,
                    ItemId = this.currentItem.DatabaseId!,
                    TrackIndex = audioTrack.Index,
                    TrackName = audioTrack.Title!
                });
                this.callInProgress = false;

                if (!response.IsSuccessResult())
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

            var updateResponse = await this.ContributionClient.EditItemOnDisc.ExecuteAsync(new EditItemOnDiscInput
            {
                ContributionId = this.ContributionId!,
                DiscId = this.DiscId!,
                ItemId = this.currentItem.DatabaseId!,
                ChapterCount = updateRequest.ChapterCount,
                Description = updateRequest.Description,
                Duration = updateRequest.Duration,
                Name = updateRequest.Name,
                SegmentCount = updateRequest.SegmentCount,
                SegmentMap = updateRequest.SegmentMap,
                Size = updateRequest.Size,
                Source = updateRequest.Source,
                Type = updateRequest.Type,
                Season = updateRequest.Season,
                Episode = updateRequest.Episode
            });
            callInProgress = false;

            if (updateResponse.IsSuccessResult())
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
        var addRequest = currentItem.CreateAddRequest();
        var response = await this.ContributionClient.AddItemToDisc.ExecuteAsync(new AddItemToDiscInput
        {
            ContributionId = this.ContributionId!,
            DiscId = this.DiscId!,
            ChapterCount = addRequest.ChapterCount,
            Description = addRequest.Description,
            Duration = addRequest.Duration,
            Name = addRequest.Name,
            SegmentCount = addRequest.SegmentCount,
            SegmentMap = addRequest.SegmentMap,
            Size = addRequest.Size,
            Source = addRequest.Source,
            Type = addRequest.Type,
            Season = addRequest.Season,
            Episode = addRequest.Episode
        });
        callInProgress = false;

        if (response?.DataInfo != null && response.IsSuccessResult())
        {
            currentItem.DatabaseId = response.Data!.AddItemToDisc.UserContributionDiscItem!.EncodedId;
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
        var addRequest = currentItem.CreateAddRequest();
        var response = await this.ContributionClient.AddItemToDisc.ExecuteAsync(new AddItemToDiscInput
        {
            ContributionId = this.ContributionId!,
            DiscId = this.DiscId!,
            ChapterCount = addRequest.ChapterCount,
            Description = addRequest.Description,
            Duration = addRequest.Duration,
            Name = addRequest.Name,
            SegmentCount = addRequest.SegmentCount,
            SegmentMap = addRequest.SegmentMap,
            Size = addRequest.Size,
            Source = addRequest.Source,
            Type = addRequest.Type,
            Season = addRequest.Season,
            Episode = addRequest.Episode
        });
            //this.ContributionId!, this.DiscId!, currentItem.CreateAddRequest());
        this.callInProgress = false;

        if (response?.Data?.AddItemToDisc != null && response.IsSuccessResult())
        {
            currentItem.DatabaseId = response.Data.AddItemToDisc.UserContributionDiscItem!.EncodedId;
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

        var match = TryFindEpisodeName(this.episodeNames, this.currentItem.Episode.Season, this.currentItem.Episode.Episode);
        if (match != null)
        {
            this.currentItem.ItemTitle = match.EpisodeName;
        }
    }

    private IGetEpisodeNames_EpisodeNames_SeriesEpisodeNames_Episodes? TryFindEpisodeName(IGetEpisodeNames_EpisodeNames episodeNames, string season, string episode)
    {
        return episodeNames.SeriesEpisodeNames!.Episodes.FirstOrDefault(e =>
            string.Equals(e.SeasonNumber, season, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.EpisodeNumber, episode, StringComparison.OrdinalIgnoreCase));
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
