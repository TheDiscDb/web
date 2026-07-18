using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class Partials : ComponentBase, IDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "type")]
    public string? TypeFilter { get; set; }

    private readonly CancellationTokenSource cts = new();
    private CancellationToken ComponentCt => this.cts.Token;

    private List<PartialReleaseEntry>? partialReleases;
    private List<PartialDiscEntry>? partialDiscs;
    private string? errorMessage;

    private bool ShowReleases => string.IsNullOrEmpty(TypeFilter) || TypeFilter.Equals("releases", StringComparison.OrdinalIgnoreCase);
    private bool ShowDiscs => string.IsNullOrEmpty(TypeFilter) || TypeFilter.Equals("discs", StringComparison.OrdinalIgnoreCase);

    private string FilterClass(string? filter)
    {
        bool active = string.Equals(TypeFilter, filter, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrEmpty(TypeFilter) && string.IsNullOrEmpty(filter));
        return active ? "partials-filter-link active" : "partials-filter-link";
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            errorMessage = null;
            await using var db = await DbFactory.CreateDbContextAsync(this.ComponentCt);

            // Partial releases: releases with at least one placeholder (known-missing) disc.
            partialReleases = await db.Releases
                .Where(r => r.IsPartial && r.MediaItem != null)
                .OrderBy(r => r.MediaItem!.SortTitle ?? r.MediaItem.Title)
                .ThenBy(r => r.Title)
                .Select(r => new PartialReleaseEntry
                {
                    MediaItemType = r.MediaItem!.Type!,
                    MediaItemSlug = r.MediaItem.Slug!,
                    MediaItemTitle = r.MediaItem.FullTitle,
                    ReleaseTitle = r.Title,
                    ReleaseSlug = r.Slug!,
                    MissingDiscs = r.Discs
                        .Where(d => d.Disc != null && d.Disc.IsPlaceholder)
                        .Select(d => new MissingDisc(d.Name, d.Disc!.Format))
                        .ToList()
                })
                .ToListAsync(this.ComponentCt);

            // Partial discs: discs that have logs but are not fully identified (not placeholders).
            // Discs are shared across releases; project one linkable release per disc.
            var discRows = await db.ReleaseDiscs
                .Where(rd => rd.Disc != null && rd.Disc.IsPartial && !rd.Disc.IsPlaceholder
                    && rd.Release != null && rd.Release.MediaItem != null)
                .Select(rd => new
                {
                    rd.DiscId,
                    DiscName = rd.Name,
                    Format = rd.Disc!.Format,
                    DiscSlug = rd.Slug,
                    DiscIndex = rd.Index,
                    MediaItemType = rd.Release!.MediaItem!.Type!,
                    MediaItemSlug = rd.Release.MediaItem.Slug!,
                    MediaItemTitle = rd.Release.MediaItem.FullTitle,
                    ReleaseTitle = rd.Release.Title,
                    ReleaseSlug = rd.Release.Slug!
                })
                .ToListAsync(this.ComponentCt);

            partialDiscs = discRows
                .GroupBy(r => r.DiscId)
                .Select(g => g.First())
                .Select(r => new PartialDiscEntry
                {
                    DiscName = string.IsNullOrEmpty(r.DiscName) ? $"Disc {r.DiscIndex}" : r.DiscName,
                    Format = r.Format,
                    MediaItemTitle = r.MediaItemTitle,
                    ReleaseTitle = r.ReleaseTitle,
                    Url = $"/{r.MediaItemType.ToLowerInvariant()}/{r.MediaItemSlug}/releases/{r.ReleaseSlug}/discs/{(string.IsNullOrEmpty(r.DiscSlug) ? r.DiscIndex.ToString() : r.DiscSlug)}"
                })
                .OrderBy(d => d.MediaItemTitle)
                .ToList();
        }
        catch
        {
            errorMessage = "Unable to load partial contributions. Please try again later.";
            partialReleases = [];
            partialDiscs = [];
        }
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }

    private sealed record MissingDisc(string? Name, string? Format);

    private sealed record PartialReleaseEntry
    {
        public string MediaItemType { get; init; } = string.Empty;
        public string MediaItemSlug { get; init; } = string.Empty;
        public string MediaItemTitle { get; init; } = string.Empty;
        public string? ReleaseTitle { get; init; }
        public string ReleaseSlug { get; init; } = string.Empty;
        public IReadOnlyList<MissingDisc> MissingDiscs { get; init; } = Array.Empty<MissingDisc>();

        public string Url => $"/{MediaItemType.ToLowerInvariant()}/{MediaItemSlug}/releases/{ReleaseSlug}";
    }

    private sealed record PartialDiscEntry
    {
        public string DiscName { get; init; } = string.Empty;
        public string? Format { get; init; }
        public string MediaItemTitle { get; init; } = string.Empty;
        public string? ReleaseTitle { get; init; }
        public string Url { get; init; } = string.Empty;
    }
}
