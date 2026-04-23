using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Contribute;

[Authorize]
public partial class EngramDetails : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public string? ReleaseId { get; set; }

    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    private IContributionHistoryService HistoryService { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = null!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    private IStaticAssetStore AssetStore => ServiceProvider.GetRequiredService<IStaticAssetStore>();
    private IStaticAssetStore ImageStore => ServiceProvider.GetRequiredKeyedService<IStaticAssetStore>(KeyedServiceNames.ImagesAssetStore);

    private List<EngramSubmission> Submissions { get; set; } = new();
    private int? ExistingContributionId { get; set; }
    private string? ExistingContributionEncodedId { get; set; }
    private bool IsCreating { get; set; }
    private string? ErrorMessage { get; set; }
    private SqlServerDataContext? database;

    private readonly CreateFromEngramRequest request = new();

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrEmpty(ReleaseId))
        {
            database = await DbFactory.CreateDbContextAsync();
            Submissions = await database.EngramSubmissions
                .Include(s => s.Titles)
                .Where(s => s.ReleaseId == ReleaseId)
                .OrderBy(s => s.DiscNumber ?? int.MaxValue)
                .ThenByDescending(s => s.ReceivedAt)
                .ToListAsync();

            // Deduplicate by ContentHash — keep the most recent submission per hash
            Submissions = Submissions
                .GroupBy(s => s.ContentHash)
                .Select(g => g.First())
                .OrderBy(s => s.DiscNumber ?? int.MaxValue)
                .ToList();

            // Check if already linked to a contribution
            var linked = Submissions.FirstOrDefault(s => s.UserContributionId != null);
            if (linked != null)
            {
                ExistingContributionId = linked.UserContributionId;
                ExistingContributionEncodedId = IdEncoder.Encode(linked.UserContributionId!.Value);
            }

            PreFillFromEngram();
        }
    }

    private void PreFillFromEngram()
    {
        var first = Submissions.FirstOrDefault();
        if (first == null) return;

        if (!string.IsNullOrEmpty(first.Upc))
            request.Upc = first.Upc;

        if (!string.IsNullOrEmpty(first.DetectedTitle))
            request.Title = first.DetectedTitle;

        if (first.TmdbId.HasValue)
        {
            request.ExternalId = first.TmdbId.Value.ToString();
            request.ExternalProvider = "TMDB";
        }

        request.MediaType = InferMediaType(first);
        request.FrontImageUrl = first.FrontImageUrl ?? "";
        request.BackImageUrl = first.BackImageUrl ?? "";

        if (!string.IsNullOrEmpty(request.Title))
        {
            request.ReleaseTitle = request.Title;
            request.ReleaseSlug = request.Title.Slugify();
        }
    }

    private static string InferMediaType(EngramSubmission submission)
    {
        if (submission.DetectedSeason.HasValue)
            return "series";

        return "movie";
    }

    private static string MapContentTypeToFormat(string contentType)
    {
        return contentType?.ToLowerInvariant() switch
        {
            "blu-ray" => "Blu-ray",
            "dvd" => "DVD",
            "4k" => "4K",
            _ => "Blu-ray"
        };
    }

    private async Task CreateContributionFromEngram()
    {
        if (IsCreating) return;
        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userId = UserManager.GetUserId(authState.User);

            if (string.IsNullOrEmpty(userId))
            {
                ErrorMessage = "You must be logged in to create a contribution.";
                return;
            }

            await using var db = await DbFactory.CreateDbContextAsync();

            // Check that submissions aren't already linked
            var alreadyLinked = await db.EngramSubmissions
                .AnyAsync(s => s.ReleaseId == ReleaseId && s.UserContributionId != null);

            if (alreadyLinked)
            {
                ErrorMessage = "This release has already been converted to a contribution.";
                return;
            }

            var contribution = new UserContribution
            {
                UserId = userId,
                Created = DateTimeOffset.UtcNow,
                Status = UserContributionStatus.Pending,
                MediaType = request.MediaType,
                ExternalId = request.ExternalId ?? "",
                ExternalProvider = request.ExternalProvider ?? "",
                ReleaseDate = request.ReleaseDate,
                Asin = request.Asin ?? "",
                Upc = request.Upc ?? "",
                FrontImageUrl = "",
                BackImageUrl = "",
                ReleaseTitle = request.ReleaseTitle ?? "",
                ReleaseSlug = request.ReleaseSlug,
                Locale = request.Locale ?? "",
                RegionCode = request.RegionCode ?? "",
                Title = request.Title,
                Year = request.Year,
                TitleSlug = CreateTitleSlug(request.Title, request.Year),
            };

            db.UserContributions.Add(contribution);
            await db.SaveChangesAsync();
            IdEncoder.EncodeInPlace(contribution);

            // Create discs and items
            int discIndex = 1;
            foreach (var submission in Submissions)
            {
                var disc = new UserContributionDisc
                {
                    ContentHash = submission.ContentHash,
                    Format = MapContentTypeToFormat(submission.ContentType),
                    Name = !string.IsNullOrEmpty(submission.VolumeLabel)
                        ? submission.VolumeLabel
                        : $"Disc {submission.DiscNumber ?? discIndex:D2}",
                    Slug = $"disc{discIndex:D2}",
                    Index = discIndex,
                    ExistingDiscPath = "",
                };

                contribution.Discs.Add(disc);
                await db.SaveChangesAsync();

                foreach (var title in submission.Titles.OrderBy(t => t.TitleIndex))
                {
                    var item = new UserContributionDiscItem
                    {
                        Name = title.SourceFilename ?? $"Title {title.TitleIndex}",
                        Source = title.SourceFilename ?? "",
                        Duration = FormatDuration(title.DurationSeconds),
                        Size = FormatSize(title.SizeBytes),
                        ChapterCount = title.ChapterCount ?? 0,
                        SegmentCount = title.SegmentCount ?? 0,
                        SegmentMap = title.SegmentMap ?? "",
                        Type = title.TitleType ?? "",
                        Season = title.Season ?? "",
                        Episode = title.Episode ?? "",
                        Description = "",
                    };

                    disc.Items.Add(item);
                }

                await db.SaveChangesAsync();
                discIndex++;
            }

            // Link engram submissions to the contribution
            var submissionIds = Submissions.Select(s => s.Id).ToList();
            await db.EngramSubmissions
                .Where(s => submissionIds.Contains(s.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserContributionId, contribution.Id));

            // Record history
            await HistoryService.RecordCreatedAsync(contribution.Id, userId);

            // Copy images to final contribution paths (best-effort)
            await CopyEngramImageToContribution(request.FrontImageUrl, contribution.EncodedId, "front", db, contribution);
            await CopyEngramImageToContribution(request.BackImageUrl, contribution.EncodedId, "back", db, contribution);

            // Copy scan logs (best-effort)
            foreach (var submission in Submissions)
            {
                var disc = contribution.Discs.FirstOrDefault(d => d.ContentHash == submission.ContentHash);
                if (disc != null)
                {
                    await CopyScanLog(submission.ScanLogPath, contribution.EncodedId, disc, db);
                }
            }

            NavigationManager.NavigateTo($"/contribution/{contribution.EncodedId}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create contribution: {ex.Message}";
        }
        finally
        {
            IsCreating = false;
        }
    }

    private async Task CopyEngramImageToContribution(string? engramImagePath, string contributionEncodedId, string name, SqlServerDataContext db, UserContribution contribution)
    {
        if (string.IsNullOrEmpty(engramImagePath))
            return;

        try
        {
            if (await ImageStore.Exists(engramImagePath))
            {
                var data = await ImageStore.Download(engramImagePath);
                if (data != null && data.ToArray().Length > 0)
                {
                    using var memoryStream = new MemoryStream(data.ToArray());

                    var imageStorePath = $"Contributions/{contributionEncodedId}/{name}.jpg";
                    await ImageStore.Save(memoryStream, imageStorePath, ContentTypes.ImageContentType);

                    memoryStream.Position = 0;
                    var assetStorePath = $"{contributionEncodedId}/{name}.jpg";
                    await AssetStore.Save(memoryStream, assetStorePath, ContentTypes.ImageContentType);

                    if (name == "front")
                        contribution.FrontImageUrl = $"/images/Contributions/{contributionEncodedId}/{name}.jpg";
                    else
                        contribution.BackImageUrl = $"/images/Contributions/{contributionEncodedId}/{name}.jpg";

                    await db.SaveChangesAsync();
                }
            }
        }
        catch
        {
            // Image copy is best-effort — user can re-upload later
        }
    }

    private async Task CopyScanLog(string? scanLogPath, string contributionEncodedId, UserContributionDisc disc, SqlServerDataContext db)
    {
        if (string.IsNullOrEmpty(scanLogPath))
            return;

        try
        {
            if (await AssetStore.Exists(scanLogPath))
            {
                var data = await AssetStore.Download(scanLogPath);
                if (data != null && data.ToArray().Length > 0)
                {
                    var targetPath = $"{contributionEncodedId}/{IdEncoder.Encode(disc.Id)}-logs.txt";
                    using var stream = new MemoryStream(data.ToArray());
                    await AssetStore.Save(stream, targetPath, ContentTypes.TextContentType);
                    disc.LogsUploaded = true;
                    await db.SaveChangesAsync();
                }
            }
        }
        catch
        {
            // Scan log copy is best-effort
        }
    }

    private static string CreateTitleSlug(string? name, string? year)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        if (!string.IsNullOrEmpty(year))
            return $"{name.Slugify()}-{year}";

        return name.Slugify();
    }

    private static string FormatDuration(int? seconds)
    {
        if (seconds == null) return "";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.Hours > 0 ? $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s" : $"{ts.Minutes}m {ts.Seconds}s";
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes == null) return "";
        return bytes.Value switch
        {
            >= 1_073_741_824 => $"{bytes.Value / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes.Value / 1_048_576.0:F1} MB",
            _ => $"{bytes.Value / 1024.0:F0} KB"
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (database != null)
            await database.DisposeAsync();
    }
}

public class CreateFromEngramRequest
{
    [Required]
    public string MediaType { get; set; } = "movie";
    public string? ExternalId { get; set; }
    public string? ExternalProvider { get; set; }
    [Required(ErrorMessage = "Release Date is required")]
    public DateTimeOffset ReleaseDate { get; set; } = DateTimeOffset.UtcNow;
    [Required(ErrorMessage = "ASIN is required")]
    [RegularExpression(@"\w{10}", ErrorMessage = "ASIN must be 10 characters")]
    public string? Asin { get; set; }
    [Required(ErrorMessage = "UPC is required")]
    [RegularExpression(@"\d{12,13}", ErrorMessage = "UPC must be 12-13 digits")]
    public string? Upc { get; set; }
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    [Required(ErrorMessage = "Release Title is required")]
    public string? ReleaseTitle { get; set; }
    [Required(ErrorMessage = "Release Slug is required")]
    public string? ReleaseSlug { get; set; }
    [Required(ErrorMessage = "Locale is required")]
    public string? Locale { get; set; }
    [Required(ErrorMessage = "Region Code is required")]
    public string? RegionCode { get; set; }
    public string? Title { get; set; }
    public string? Year { get; set; }
}
