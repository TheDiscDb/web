using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

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
    IDbContextFactory<SqlServerDataContext>? Context { get; set; }

    private IDisplayItem? Item { get; set; }
    private Release? Release { get; set; }
    private Disc? Disc { get; set; }
    private Title? Title { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (this.Type!.Equals("Boxset", StringComparison.OrdinalIgnoreCase))
        {
            await HandleBoxset();
        }
        else
        {
            await HandleMovieOrSeries();
        }
    }

    protected async Task HandleMovieOrSeries()
    {
        var context = await this.Context!.CreateDbContextAsync();
        var item = context.MediaItems
            .Include("Releases")
            .Include("Releases.Discs")
            .Include("Releases.Discs.Titles")
            .Include("Releases.Discs.Titles.Item")
            .Include("Releases.Discs.Titles.Item.Chapters")
            .Include("Releases.Discs.Titles.Tracks")
            .FirstOrDefault(i => i.Type == Type && i.Slug == Slug);
        Item = item;

        if (Item != null)
        {
            Release = item?.Releases.FirstOrDefault(r => r.Slug == ReleaseSlug);

            if (Release != null)
            {
                Disc = Release.Discs.FirstOrDefault(d => TheDiscDb.SlugOrIndex.Create(d.Slug, d.Index) == TheDiscDb.SlugOrIndex.Create(SlugOrIndex));

                if (Disc != null && !string.IsNullOrEmpty(File))
                {
                    string sourceFile = NavigationExtensions.GetSourceFile(this.File, this.Extension);
                    Title = Disc.Titles.FirstOrDefault(t => t.SourceFile == sourceFile);
                }
            }
        }
    }

    protected async Task HandleBoxset()
    {
        var context = await this.Context!.CreateDbContextAsync();
        var boxset = context.BoxSets
            .Include("Release")
            .Include("Release.Discs")
            .Include("Release.Discs.Titles")
            .Include("Release.Discs.Titles.Item")
            .Include("Release.Discs.Titles.Item.Chapters")
            .Include("Release.Discs.Titles.Tracks")
            .FirstOrDefault(i => i.Slug == Slug);
        Item = boxset;

        if (Item != null)
        {
            Release = boxset?.Release;

            if (Release != null)
            {
                Disc = Release.Discs.FirstOrDefault(d => TheDiscDb.SlugOrIndex.Create(d.Slug, d.Index) == TheDiscDb.SlugOrIndex.Create(SlugOrIndex));

                if (Disc != null && !string.IsNullOrEmpty(File) && !string.IsNullOrEmpty(Extension))
                {
                    string sourceFile = NavigationExtensions.GetSourceFile(this.File, this.Extension);
                    Title = Disc.Titles.FirstOrDefault(t => t.SourceFile == sourceFile);
                }
            }
        }
    }
}