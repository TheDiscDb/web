using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
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

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private MediaItem? Item { get; set; }
    private IEnumerable<Release> Releases { get; set; } = new List<Release>();

    private bool HasAnyFacts =>
        !string.IsNullOrWhiteSpace(Item?.ContentRating) ||
        !string.IsNullOrWhiteSpace(Item?.Genres) ||
        !string.IsNullOrWhiteSpace(Item?.Runtime);

    private bool HasAnyPeople =>
        !string.IsNullOrWhiteSpace(Item?.Directors) ||
        !string.IsNullOrWhiteSpace(Item?.Stars) ||
        !string.IsNullOrWhiteSpace(Item?.Writers);

    private List<Group> CustomGroups =>
        Item?.MediaItemGroups
            .Where(mig => mig.Group != null && !string.IsNullOrEmpty(mig.Group.Slug))
            .Where(mig => string.Equals(mig.Role, "CustomGroup", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(mig.Role, "Genre", StringComparison.OrdinalIgnoreCase))
            .Select(mig => mig.Group!)
            .DistinctBy(g => g.Slug)
            .OrderBy(g => g.Name)
            .ToList() ?? [];

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
        else if (HttpContext != null && !HttpContext.Response.HasStarted)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
