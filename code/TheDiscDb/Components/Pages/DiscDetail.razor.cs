using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
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

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private MediaItem? Item { get; set; }
    private Boxset? BoxsetItem { get; set; }
    private Release? DiscRelease { get; set; }
    private Disc? Disc { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // Detect the boxset disc case (legacy URL: /boxset/{slug}/discs/{slugOrIndex})
        bool isBoxsetUrl = false;
        if (string.IsNullOrEmpty(this.Type) && !string.IsNullOrEmpty(this.Slug) && !string.IsNullOrEmpty(this.SlugOrIndexString))
        {
            this.Type = "boxset";
            isBoxsetUrl = true;
        }

        // Also handle boxset via /{type}/... route
        if (this.Type?.Equals("boxset", StringComparison.OrdinalIgnoreCase) == true)
        {
            isBoxsetUrl = true;
        }

        if (Cache == null || string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Slug))
        {
            return;
        }

        if (isBoxsetUrl)
        {
            BoxsetItem = await Cache.GetBoxsetAsync(Slug);
            this.DiscRelease = BoxsetItem?.Release;

            if (this.DiscRelease != null)
            {
                this.Disc = DiscRelease.Discs.FirstOrDefault(d =>
                        SlugOrIndex.Create(d.Slug, d.Index) == SlugOrIndex.Create(SlugOrIndexString));
            }
        }
        else
        {
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

        if (DiscRelease == null || Disc == null)
        {
            if (HttpContext != null && !HttpContext.Response.HasStarted)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
    }
}