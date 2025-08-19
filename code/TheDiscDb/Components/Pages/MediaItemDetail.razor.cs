using Microsoft.AspNetCore.Components;
using SixLabors.ImageSharp.Web.Caching;
using TheDiscDb.InputModels;

namespace TheDiscDb.Components.Pages;

public partial class MediaItemDetail : ComponentBase
{
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Inject]
    public CacheHelper? Cache { get; set; }

    private MediaItem? Item { get; set; }
    private IEnumerable<Release> Releases { get; set; } = new List<Release>();

    protected override async Task OnInitializedAsync()
    {
        if (this.Cache == null)
        {
            throw new Exception("Cache was not injected");
        }

        string cacheKey = $"MediaItemDetail|{Type}|{Slug}";

        if (this.Type != null && this.Slug != null)
        {
            this.Item = await this.Cache.GetMediaItemDetail(this.Type, this.Slug);
        }

        if (Item != null)
        {
            Releases = Item.Releases;
        }
    }
}
