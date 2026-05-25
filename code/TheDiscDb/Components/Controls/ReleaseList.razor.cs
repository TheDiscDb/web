using Microsoft.AspNetCore.Components;
using TheDiscDb.Affiliate;
using TheDiscDb.InputModels;

namespace TheDiscDb.Components.Controls;

public partial class ReleaseList : ComponentBase
{
    [Parameter]
    public IDisplayItem? Item { get; set; }

    [Parameter]
    public IEnumerable<Release> Releases { get; set; } = new List<Release>();

    [Inject]
    public IGruvLinkLookup GruvLookup { get; set; } = null!;

    internal string? ParentMediaItemSlug => this.Item is Boxset ? null : this.Item?.Slug;

    internal string? ParentBoxsetSlug => this.Item is Boxset ? this.Item?.Slug : null;

    protected override async Task OnParametersSetAsync()
    {
        // Batch-load all gruv affiliate rows for the releases we're about to render. Each
        // <GruvBuyButton> below will call GetAsync, but those calls will all be cache hits.
        if (this.Item is null)
        {
            return;
        }

        var releaseSlugs = this.Releases
            .Select(r => r.Slug)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToList();
        if (releaseSlugs.Count == 0)
        {
            return;
        }

        await this.GruvLookup.PreloadAsync(this.ParentMediaItemSlug, this.ParentBoxsetSlug, releaseSlugs, CancellationToken.None);
    }
}

