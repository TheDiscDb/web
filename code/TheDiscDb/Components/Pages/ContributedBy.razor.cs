using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class ContributedBy : ComponentBase
{
    [Parameter]
    public string? Username { get; set; }

    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    private List<ContributedRelease>? releases;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(Username))
        {
            errorMessage = "No contributor specified.";
            releases = [];
            return;
        }

        try
        {
            await using var db = await DbFactory.CreateDbContextAsync();

            releases = await db.Releases
                .Where(r => r.Contributors.Any(c => c.Name == Username))
                .Where(r => r.MediaItem != null)
                .Select(r => new ContributedRelease
                {
                    Title = r.MediaItem!.Title ?? r.Title ?? "Unknown",
                    Year = r.MediaItem!.Year,
                    ImageUrl = r.ImageUrl ?? r.MediaItem!.ImageUrl,
                    MediaItemSlug = r.MediaItem!.Slug ?? "",
                    MediaType = (r.MediaItem!.Type ?? "movie").ToLower(),
                    ReleaseTitle = r.Title ?? "",
                    DateAdded = r.DateAdded
                })
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();
        }
        catch
        {
            errorMessage = "Unable to load releases. Please try again later.";
            releases = [];
        }
    }

    private record ContributedRelease
    {
        public string Title { get; init; } = "";
        public int Year { get; init; }
        public string? ImageUrl { get; init; }
        public string MediaItemSlug { get; init; } = "";
        public string MediaType { get; init; } = "movie";
        public string ReleaseTitle { get; init; } = "";
        public DateTimeOffset DateAdded { get; init; }
    }
}
