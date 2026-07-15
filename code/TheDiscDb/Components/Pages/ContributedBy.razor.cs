using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.Achievements;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class ContributedBy : ComponentBase, IDisposable
{
    [Parameter]
    public string? Username { get; set; }

    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IAchievementService AchievementService { get; set; } = null!;

    private readonly CancellationTokenSource cts = new();
    private CancellationToken ComponentCt => this.cts.Token;

    private List<ContributedRelease>? releases;
    private UserAchievementProfile? profile;
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
            await using var db = await DbFactory.CreateDbContextAsync(this.ComponentCt);

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
                .ToListAsync(this.ComponentCt);
        }
        catch
        {
            errorMessage = "Unable to load releases. Please try again later.";
            releases = [];
        }

        try
        {
            profile = await AchievementService.GetProfileAsync(Username!, this.ComponentCt);
        }
        catch
        {
            // Achievements are decorative; a failure here must not break the profile page.
            profile = null;
        }
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }

    // Buckets a 0-100 percentage to the nearest 5 so a fixed set of width utility classes
    // (pw-0 .. pw-100 in the scoped CSS) can render the bar without an inline style.
    private static int ProgressBucket(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        return (int)(Math.Round(clamped / 5.0) * 5);
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
