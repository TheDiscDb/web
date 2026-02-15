using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class DiscDetail : ComponentBase
{
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

    [Parameter]
    public string? SlugOrIndexString { get; set; }

    [Parameter]
    public string? ContentHash { get; set; }

    [Inject]
    public CacheHelper? Cache { get; set; }

    private MediaItem? Item { get; set; }
    private Release? DiscRelease { get; set; }
    private Disc? Disc { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Cache == null || string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Slug))
        {
            return;
        }

        Item = await Cache.GetMediaItemDetail(Type, Slug);

        if (Item != null && !string.IsNullOrEmpty(ReleaseSlug))
        {
            DiscRelease = Item.Releases.FirstOrDefault(r => r.Slug == ReleaseSlug);

            if (DiscRelease != null && !string.IsNullOrEmpty(SlugOrIndexString))
            {
                Disc = DiscRelease.Discs.FirstOrDefault(d =>
                    SlugOrIndex.Create(d.Slug, d.Index) == SlugOrIndex.Create(SlugOrIndexString));
            }
        }
    }
}
