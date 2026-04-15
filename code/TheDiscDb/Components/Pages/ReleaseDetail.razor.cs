using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class ReleaseDetail : ComponentBase
{
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

    [Inject]
    public CacheHelper? Cache {  get; set; }

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private MediaItem? Item { get; set; }
    private Release? Release { get; set; }

    private List<Group> AllGroups
    {
        get
        {
            var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);

            if (Item?.MediaItemGroups != null)
            {
                foreach (var mig in Item.MediaItemGroups)
                {
                    if (mig.Group?.Slug != null)
                    {
                        groups.TryAdd(mig.Group.Slug, mig.Group);
                    }
                }
            }

            if (Release?.ReleaseGroups != null)
            {
                foreach (var rg in Release.ReleaseGroups)
                {
                    if (rg.Group?.Slug != null)
                    {
                        groups.TryAdd(rg.Group.Slug, rg.Group);
                    }
                }
            }

            return [.. groups.Values.OrderBy(g => g.Name)];
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (this.Cache == null)
        {
            throw new Exception("Cache was not injected");
        }

        if (this.Type != null && this.Slug != null)
        {
            this.Item = await this.Cache.GetMediaItemDetail(Type, Slug);
        }

        if (Item != null)
        {
            Release = Item.Releases.FirstOrDefault(r => !string.IsNullOrEmpty(r.Slug) && r.Slug.Equals(ReleaseSlug, StringComparison.OrdinalIgnoreCase));
        }

        if (Item == null || Release == null)
        {
            if (HttpContext != null && !HttpContext.Response.HasStarted)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
    }
}
