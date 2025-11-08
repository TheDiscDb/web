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

public class ItemIdentification
{
    public required Title Title { get; set; }
    [Required]
    public required string ItemTitle { get; set; }
    public string? Description { get; set; }
    public required string Type { get; set; }
    public EpisodeIdentification? Episode { get; set; }
    public string? DatabaseId { get; set; }
    public List<ChapterItem> Chapters { get; set; } = new List<ChapterItem>();

    public AddItemRequest CreateAddRequest()
    {
        return new AddItemRequest
        {
            ChapterCount = Title.ChapterCount,
            Description = Description,
            Size = Title.DisplaySize!,
            Duration = Title.Length!,
            Name = ItemTitle,
            SegmentCount = Title.Segments.Count,
            SegmentMap = Title.SegmentMap!,
            Source = Title.Playlist!,
            Type = Type,
            Season = Episode != null ? Episode.Season : null,
            Episode = Episode != null ? Episode.Episode : null
        };
    }

    public void InitializeChapters()
    {
        // If there are already chapters, nothing to initialize
        if (this.Chapters.Count > 0)
        {
            return;
        }
         
        if (this.Title.ChapterCount > 0)
        {
            this.Chapters = new List<ChapterItem>();
            for (int i = 0; i < this.Title.ChapterCount; i++)
            {
                this.Chapters.Add(new ChapterItem
                {
                    Index = i + 1,
                    Title = ""
                });
            }
        }
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

    private IQueryable<MakeMkv.Title>? titles = null;
    private UserContributionDisc? disc = null;
    private readonly Dictionary<Title, ItemIdentification> identifiedTitles = new Dictionary<Title, ItemIdentification>();

    bool showEpisodeDialog = false;
    SfDialog? episodeDialogObj;

    bool showItemDialog = false;
    SfDialog? itemDialogObj;

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
        if (response?.Value != null)
        {
            this.titles = response.Value.Info!.Titles.AsQueryable();
            this.disc = response.Value.Disc;

            if (disc?.Items != null)
            {
                foreach (var item in disc.Items)
                {
                    var title = this.titles.FirstOrDefault(t => t.SegmentMap == item.SegmentMap && t.ChapterCount == item.ChapterCount && t.DisplaySize == item.Duration);
                    if (title != null)
                    {
                        var existingItem = new ItemIdentification
                        {
                            DatabaseId = item.Id.ToString(),
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

                        identifiedTitles[title] = existingItem;
                    }
                }
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

    async Task NameAudioTracks(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            this.showAudioTrackDialog = true;
        }
    }

    async Task InputChapters(Title title)
    {
        if (identifiedTitles.TryGetValue(title, out var item))
        {
            item.InitializeChapters();
            this.showChapterDialog = true;
        }
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
            // Pop up dialog to ask for the title and description
            this.showItemDialog = true;
        }

        return Task.CompletedTask;
    }

    public async Task HandleValidChapterSubmit()
    {
        if (this.currentItem == null)
        {
            return;
        }

        foreach (var chapter in this.currentItem.Chapters)
        {
            if (!string.IsNullOrEmpty(chapter.Title))
            {
                var response = await this.Client.AddChapterToItem(this.ContributionId!, this.DiscId!, this.currentItem.DatabaseId!, new AddChapterRequest
                {
                    Index = chapter.Index,
                    Title = chapter.Title!
                });

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
        if (this.currentItem == null)
        {
            return;
        }

        await this.audioTrackDialog!.HideAsync();
    }

    public async Task HandleValidItemSubmit()
    {
        if (this.currentItem == null)
        {
            return;
        }

        var response = await this.Client.AddItemToDisc(this.ContributionId!, this.DiscId!, currentItem.CreateAddRequest());

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

        await this.itemDialogObj!.HideAsync();
    }

    private async Task ItemDialogCancelClicked()
    {
        this.identifiedTitles.Remove(this.currentItem!.Title!);
        this.StateHasChanged();
        await this.itemDialogObj!.HideAsync();
    }

    private async Task ChapterDialogCancelClicked()
    {
        this.currentItem!.Chapters.Clear();
        await this.chapterDialog!.HideAsync();
        this.StateHasChanged();
    }

    private async Task AudioTrackDialogCancelClicked()
    {
        await this.audioTrackDialog!.HideAsync();
        this.StateHasChanged();
    }

    public async Task HandleValidEpisodeSubmit()
    {
        if (this.currentItem == null)
        {
            return;
        }

        var response = await this.Client.AddItemToDisc(this.ContributionId!, this.DiscId!, currentItem.CreateAddRequest());

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

        await this.episodeDialogObj!.HideAsync();
    }

    private async Task EpisodeDialogCancelClicked()
    {
        this.identifiedTitles.Remove(this.currentItem!.Title!);
        this.StateHasChanged();
        await this.episodeDialogObj!.HideAsync();
    }

    private void SubmitIdentifications(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        this.NavigationManager.NavigateTo($"/contribution/{this.ContributionId}");
    }
}
