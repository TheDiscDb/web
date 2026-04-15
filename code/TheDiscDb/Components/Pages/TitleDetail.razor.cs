using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using TheDiscDb.InputModels;

namespace TheDiscDb.Components.Pages;

public partial class TitleDetail : ComponentBase
{
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

    [Parameter]
    public string? SlugOrIndex { get; set; }

    [Parameter]
    public string? File { get; set; }

    [Parameter]
    public string? Extension { get; set; }

    [Inject]
    public CacheHelper? Cache { get; set; }

    [CascadingParameter]
    public HttpContext? HttpContext { get; set; }

    private IDisplayItem? Item { get; set; }
    private Release? Release { get; set; }
    private Disc? Disc { get; set; }
    private Title? Title { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Cache == null || string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Slug))
        {
            return;
        }

        if (this.Type.Equals("Boxset", StringComparison.OrdinalIgnoreCase))
        {
            await HandleBoxset();
        }
        else
        {
            await HandleMovieOrSeries();
        }

        if (Item == null || Release == null || Disc == null || Title == null)
        {
            if (HttpContext != null && !HttpContext.Response.HasStarted)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        }
    }

    private async Task HandleMovieOrSeries()
    {
        var item = await Cache!.GetMediaItemDetail(Type!, Slug!);
        Item = item;

        if (item == null)
        {
            return;
        }

        Release = item.Releases.FirstOrDefault(r =>
            !string.IsNullOrEmpty(r.Slug) && r.Slug.Equals(ReleaseSlug, StringComparison.OrdinalIgnoreCase));

        if (Release == null)
        {
            return;
        }

        Disc = Release.Discs.FirstOrDefault(d =>
            TheDiscDb.SlugOrIndex.Create(d.Slug, d.Index) == TheDiscDb.SlugOrIndex.Create(SlugOrIndex));

        if (Disc != null && !string.IsNullOrEmpty(File))
        {
            string sourceFile = NavigationExtensions.GetSourceFile(this.File, this.Extension);
            Title = Disc.Titles.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.SourceFile) && t.SourceFile.Equals(sourceFile, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task HandleBoxset()
    {
        var boxset = await Cache!.GetBoxsetAsync(Slug!);
        Item = boxset;

        if (boxset == null)
        {
            return;
        }

        Release = boxset.Release;

        if (Release == null)
        {
            return;
        }

        Disc = Release.Discs.FirstOrDefault(d =>
            TheDiscDb.SlugOrIndex.Create(d.Slug, d.Index) == TheDiscDb.SlugOrIndex.Create(SlugOrIndex));

        if (Disc != null && !string.IsNullOrEmpty(File))
        {
            string sourceFile = NavigationExtensions.GetSourceFile(this.File, this.Extension);
            Title = Disc.Titles.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.SourceFile) && t.SourceFile.Equals(sourceFile, StringComparison.OrdinalIgnoreCase));
        }
    }
}