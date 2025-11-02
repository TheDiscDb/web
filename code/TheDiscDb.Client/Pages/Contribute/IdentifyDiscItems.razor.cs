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

public class ItemIdentification
{
    public required Title Title { get; set; }
    [Required]
    public required string ItemTitle { get; set; }
    public string? Description { get; set; }
    public required string Type { get; set; }
    public EpisodeIdentification? Episode { get; set; }
    public string? DatabaseId { get; set; }

    public AddItemRequest CreateAddRequest()
    {
        return new AddItemRequest
        {
            ChapterCount = Title.ChapterCount,
            Description = Description,
            Duration = Title.DisplaySize!,
            Name = ItemTitle,
            SegmentCount = Title.Segments.Count,
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

    private IQueryable<MakeMkv.Title>? titles = null;
    private UserContributionDisc? disc = null;
    private readonly Dictionary<Title, ItemIdentification> identifiedTitles = new Dictionary<Title, ItemIdentification>();

    bool showEpisodeDialog = false;
    bool showItemDialog = false;

    SfDialog? episodeDialogObj;
    SfDialog? itemDialogObj;
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
