using System.ComponentModel.DataAnnotations;
using Fantastic.TheMovieDb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Validation;
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

    [Inject]
    private TheMovieDbClient TmdbClient { get; set; } = null!;

    [Inject]
    private ILogger<EngramDetails> Logger { get; set; } = null!;

    private IStaticAssetStore AssetStore => ServiceProvider.GetRequiredService<IStaticAssetStore>();
    private IStaticAssetStore ImageStore => ServiceProvider.GetRequiredKeyedService<IStaticAssetStore>(KeyedServiceNames.ImagesAssetStore);

    private List<EngramDisc> Submissions { get; set; } = new();
    private EngramRelease? EngramRelease { get; set; }
    private int? ExistingContributionId { get; set; }
    private string? ExistingContributionEncodedId { get; set; }
    private bool IsCreating { get; set; }
    private string? ErrorMessage { get; set; }
    private SqlServerDataContext? database;
    private readonly CancellationTokenSource cts = new();
    private CancellationToken ComponentCt => this.cts.Token;

    // TMDB-resolved metadata for the page header (preview only — not persisted as-is).
    private string? TmdbTitle { get; set; }
    private int? TmdbYear { get; set; }
    private string? TmdbPosterUrl { get; set; }
    private bool TmdbLookupFailed { get; set; }

    private readonly CreateFromEngramRequest request = new();
    private TheDiscDb.Client.Controls.ReleaseDateInput? releaseDateInput;

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(ReleaseId))
        {
            return;
        }

        database = await DbFactory.CreateDbContextAsync(this.ComponentCt);
        Submissions = await database.EngramDiscs
            .Include(s => s.Titles)
            .Where(s => s.EngramRelease!.ReleaseId == ReleaseId)
            .OrderBy(s => s.DiscNumber ?? int.MaxValue)
            .ThenByDescending(s => s.ReceivedAt)
            .ToListAsync(this.ComponentCt);

        EngramRelease = await database.EngramReleases
            .FirstOrDefaultAsync(r => r.ReleaseId == ReleaseId, this.ComponentCt);

        // Deduplicate by ContentHash — keep the most recent submission per hash.
        Submissions = Submissions
            .GroupBy(s => s.ContentHash)
            .Select(g => g.First())
            .OrderBy(s => s.DiscNumber ?? int.MaxValue)
            .ToList();

        if (EngramRelease?.UserContributionId != null)
        {
            ExistingContributionId = EngramRelease.UserContributionId;
            ExistingContributionEncodedId = IdEncoder.Encode(EngramRelease.UserContributionId.Value);
            return;
        }

        PreFillFromEngram();
        await TryLoadTmdbAsync();
    }

    private void PreFillFromEngram()
    {
        var first = Submissions.FirstOrDefault();
        if (first == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(first.Upc))
        {
            request.Upc = first.Upc;
        }

        request.MediaType = InferMediaType(first);
    }

    private async Task TryLoadTmdbAsync()
    {
        var first = Submissions.FirstOrDefault();
        if (first?.TmdbId is null)
        {
            return;
        }

        try
        {
            if (request.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
            {
                var series = await TmdbClient.GetSeries(first.TmdbId.Value.ToString(), cancellationToken: this.ComponentCt);
                if (series != null)
                {
                    TmdbTitle = series.Name;
                    TmdbYear = series.FirstAirDate?.Year;
                    if (!string.IsNullOrEmpty(series.PosterPath))
                    {
                        TmdbPosterUrl = $"https://image.tmdb.org/t/p/w500{series.PosterPath}";
                    }
                }
            }
            else
            {
                var movie = await TmdbClient.GetMovie(first.TmdbId.Value.ToString(), cancellationToken: this.ComponentCt);
                if (movie != null)
                {
                    TmdbTitle = movie.Title;
                    TmdbYear = movie.ReleaseDate?.Year;
                    if (!string.IsNullOrEmpty(movie.PosterPath))
                    {
                        TmdbPosterUrl = $"https://image.tmdb.org/t/p/w500{movie.PosterPath}";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "TMDB lookup failed for tmdbId={TmdbId}", first.TmdbId);
            TmdbLookupFailed = true;
        }
    }

    internal static string InferMediaType(EngramDisc submission)
    {
        if (submission.DetectedSeason.HasValue)
        {
            return "series";
        }

        return "movie";
    }

    internal static string MapContentTypeToFormat(string contentType)
    {
        return contentType?.ToLowerInvariant() switch
        {
            "blu-ray" => "Blu-ray",
            "dvd" => "DVD",
            "4k" => "4K",
            _ => "Blu-ray"
        };
    }

    private string ResolvedTitle()
    {
        if (!string.IsNullOrWhiteSpace(TmdbTitle))
        {
            return TmdbTitle!;
        }

        var first = Submissions.FirstOrDefault();
        if (first != null && !string.IsNullOrWhiteSpace(first.DetectedTitle))
        {
            return first.DetectedTitle!;
        }

        return "Untitled Release";
    }

    private string ResolvedYear()
    {
        return TmdbYear?.ToString() ?? string.Empty;
    }

    private async Task CreateContributionFromEngram()
    {
        if (IsCreating)
        {
            return;
        }

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            if (releaseDateInput != null && !releaseDateInput.Validate())
            {
                return;
            }

            if (Submissions.Count == 0)
            {
                ErrorMessage = "No engram submissions exist for this release yet. Upload a disc first.";
                return;
            }

            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userId = UserManager.GetUserId(authState.User);

            if (string.IsNullOrEmpty(userId))
            {
                ErrorMessage = "You must be logged in to create a contribution.";
                return;
            }

            await using var db = await DbFactory.CreateDbContextAsync(this.ComponentCt);

            var releaseRow = await db.EngramReleases
                .FirstOrDefaultAsync(r => r.ReleaseId == ReleaseId, this.ComponentCt);

            if (releaseRow?.UserContributionId != null)
            {
                ExistingContributionId = releaseRow.UserContributionId;
                ExistingContributionEncodedId = IdEncoder.Encode(releaseRow.UserContributionId.Value);
                NavigationManager.NavigateTo($"/contribution/engram/{ExistingContributionEncodedId}");
                return;
            }

            var firstSubmission = Submissions.First();
            var resolvedTitle = ResolvedTitle();
            var resolvedYear = ResolvedYear();
            var releaseTitle = resolvedTitle;
            var releaseSlug = resolvedTitle.Slugify();
            var externalId = firstSubmission.TmdbId?.ToString() ?? string.Empty;
            var externalProvider = string.IsNullOrEmpty(externalId) ? string.Empty : "TMDB";

            // Wrap the whole creation in a transaction so any failure (including a
            // EngramRelease.ReleaseId unique-index race with a concurrent submitter)
            // rolls back the contribution and discs we created — no orphaned rows.
            await using var transaction = await db.Database.BeginTransactionAsync();

            var contribution = new UserContribution
            {
                UserId = userId,
                Created = DateTimeOffset.UtcNow,
                Status = UserContributionStatus.Pending,
                MediaType = request.MediaType,
                ExternalId = externalId,
                ExternalProvider = externalProvider,
                ReleaseDate = request.ReleaseDate ?? default,
                Asin = request.Asin ?? string.Empty,
                Upc = request.Upc ?? string.Empty,
                FrontImageUrl = string.Empty,
                BackImageUrl = string.Empty,
                ReleaseTitle = releaseTitle,
                ReleaseSlug = releaseSlug,
                Locale = request.Locale ?? string.Empty,
                RegionCode = request.RegionCode ?? string.Empty,
                Title = resolvedTitle,
                Year = resolvedYear,
                TitleSlug = CreateTitleSlug(resolvedTitle, resolvedYear),
            };

            db.UserContributions.Add(contribution);
            await db.SaveChangesAsync();
            IdEncoder.EncodeInPlace(contribution);

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
                    ExistingDiscPath = string.Empty,
                };

                contribution.Discs.Add(disc);
                await db.SaveChangesAsync();

                // Try to drive item creation from the copied MakeMKV scan log so that
                // (Size, SegmentMap, ChapterCount) line up with what IdentifyDiscItems
                // reads from the same log. Falls back to EngramTitle-only items if the
                // scan log is missing or unparseable so the contribution is still usable.
                var parsedTitles = await TryLoadParsedTitlesAsync(submission.ScanLogPath);
                if (parsedTitles is { Count: > 0 })
                {
                    AddItemsFromParsedTitles(disc, parsedTitles, submission.Titles);
                }
                else
                {
                    AddItemsFromEngramTitles(disc, submission.Titles);
                }

                await db.SaveChangesAsync();

                // Copy the scan log to its final contribution location (best-effort).
                await CopyScanLog(submission.ScanLogPath, contribution.EncodedId, disc, db);

                discIndex++;
            }

            // Link the contribution at the release level. The release row should
            // already exist (it is upserted at disc-submit time), but defensively
            // create it here if missing. Two concurrent submitters can race two
            // ways:
            //   (a) both insert a brand-new EngramRelease row → caught by the
            //       unique-index DbUpdateException, retry against the winner;
            //   (b) both update an existing unlinked row → caught by the
            //       conditional UPDATE ... WHERE UserContributionId IS NULL,
            //       which is atomic at the DB level. The loser sees affected
            //       rows == 0 and rolls back.
            try
            {
                if (releaseRow == null)
                {
                    releaseRow = new EngramRelease
                    {
                        ReleaseId = ReleaseId!,
                        ReceivedAt = DateTimeOffset.UtcNow,
                        UserContributionId = contribution.Id,
                    };
                    db.EngramReleases.Add(releaseRow);
                    await db.SaveChangesAsync();
                }
                else
                {
                    // Atomic claim — only succeeds if no one else has linked yet.
                    var releaseId = releaseRow.Id;
                    var contributionId = contribution.Id;
                    var rowsClaimed = await db.EngramReleases
                        .Where(r => r.Id == releaseId && r.UserContributionId == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.UserContributionId, contributionId));

                    if (rowsClaimed == 0)
                    {
                        // Another contribution claimed this release while we were
                        // building ours. Roll back our work and redirect.
                        await transaction.RollbackAsync();

                        var winner = await db.EngramReleases
                            .AsNoTracking()
                            .FirstAsync(r => r.Id == releaseId);

                        ExistingContributionId = winner.UserContributionId;
                        ExistingContributionEncodedId = IdEncoder.Encode(winner.UserContributionId!.Value);
                        NavigationManager.NavigateTo($"/contribution/engram/{ExistingContributionEncodedId}");
                        return;
                    }
                }
            }
            catch (DbUpdateException) when (releaseRow != null && releaseRow.Id == 0)
            {
                // Concurrent submitter inserted the EngramRelease row first.
                // Detach our orphaned in-memory entity and retry the link.
                db.Entry(releaseRow).State = Microsoft.EntityFrameworkCore.EntityState.Detached;

                var concurrent = await db.EngramReleases
                    .FirstAsync(r => r.ReleaseId == ReleaseId);

                if (concurrent.UserContributionId != null)
                {
                    await transaction.RollbackAsync();

                    ExistingContributionId = concurrent.UserContributionId;
                    ExistingContributionEncodedId = IdEncoder.Encode(concurrent.UserContributionId.Value);
                    NavigationManager.NavigateTo($"/contribution/engram/{ExistingContributionEncodedId}");
                    return;
                }

                var concurrentId = concurrent.Id;
                var contributionId = contribution.Id;
                var rowsClaimed = await db.EngramReleases
                    .Where(r => r.Id == concurrentId && r.UserContributionId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.UserContributionId, contributionId));

                if (rowsClaimed == 0)
                {
                    await transaction.RollbackAsync();

                    var winner = await db.EngramReleases
                        .AsNoTracking()
                        .FirstAsync(r => r.Id == concurrentId);

                    ExistingContributionId = winner.UserContributionId;
                    ExistingContributionEncodedId = IdEncoder.Encode(winner.UserContributionId!.Value);
                    NavigationManager.NavigateTo($"/contribution/engram/{ExistingContributionEncodedId}");
                    return;
                }

                releaseRow = concurrent;
            }

            await transaction.CommitAsync();

            await HistoryService.RecordCreatedAsync(contribution.Id, userId);

            // Copy front/back covers from Engram blobs (best-effort). Engram-uploaded
            // images are preferred because they reflect the actual physical disc; the
            // user can re-upload via the standard contribution edit flow if missing.
            await CopyEngramImageToContribution(releaseRow.FrontImageUrl, contribution.EncodedId, "front", db, contribution);
            await CopyEngramImageToContribution(releaseRow.BackImageUrl, contribution.EncodedId, "back", db, contribution);

            NavigationManager.NavigateTo($"/contribution/engram/{contribution.EncodedId}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create contribution from engram releaseId={ReleaseId}", ReleaseId);
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
        {
            return;
        }

        try
        {
            if (!await ImageStore.Exists(engramImagePath))
            {
                return;
            }

            var data = await ImageStore.Download(engramImagePath);
            if (data == null || data.ToArray().Length == 0)
            {
                return;
            }

            using var memoryStream = new MemoryStream(data.ToArray());

            var imageStorePath = $"Contributions/{contributionEncodedId}/{name}.jpg";
            await ImageStore.Save(memoryStream, imageStorePath, ContentTypes.ImageContentType);

            memoryStream.Position = 0;
            var assetStorePath = $"{contributionEncodedId}/{name}.jpg";
            await AssetStore.Save(memoryStream, assetStorePath, ContentTypes.ImageContentType);

            if (name == "front")
            {
                contribution.FrontImageUrl = $"/images/Contributions/{contributionEncodedId}/{name}.jpg";
            }
            else
            {
                contribution.BackImageUrl = $"/images/Contributions/{contributionEncodedId}/{name}.jpg";
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Engram image copy failed for {Side} of contribution {EncodedId}", name, contributionEncodedId);
        }
    }

    private async Task CopyScanLog(string? scanLogPath, string contributionEncodedId, UserContributionDisc disc, SqlServerDataContext db)
    {
        if (string.IsNullOrEmpty(scanLogPath))
        {
            return;
        }

        try
        {
            if (!await AssetStore.Exists(scanLogPath))
            {
                return;
            }

            var data = await AssetStore.Download(scanLogPath);
            if (data == null || data.ToArray().Length == 0)
            {
                return;
            }

            var targetPath = $"{contributionEncodedId}/{IdEncoder.Encode(disc.Id)}-logs.txt";
            using var stream = new MemoryStream(data.ToArray());
            await AssetStore.Save(stream, targetPath, ContentTypes.TextContentType);
            disc.LogsUploaded = true;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Engram scan log copy failed for contribution {EncodedId}", contributionEncodedId);
        }
    }

    internal static string CreateTitleSlug(string? name, string? year)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(year))
        {
            return $"{name.Slugify()}-{year}";
        }

        return name.Slugify();
    }

    internal static string FormatDuration(int? seconds)
    {
        if (seconds == null)
        {
            return string.Empty;
        }

        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.Hours > 0 ? $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s" : $"{ts.Minutes}m {ts.Seconds}s";
    }

    internal static string FormatSize(long? bytes)
    {
        if (bytes == null)
        {
            return string.Empty;
        }

        return bytes.Value switch
        {
            >= 1_073_741_824 => $"{bytes.Value / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes.Value / 1_048_576.0:F1} MB",
            _ => $"{bytes.Value / 1024.0:F0} KB"
        };
    }

    private async Task<List<MakeMkv.Title>?> TryLoadParsedTitlesAsync(string? scanLogPath)
    {
        if (string.IsNullOrEmpty(scanLogPath))
        {
            return null;
        }

        try
        {
            if (!await AssetStore.Exists(scanLogPath))
            {
                return null;
            }

            var data = await AssetStore.Download(scanLogPath);
            if (data == null)
            {
                return null;
            }

            var bytes = data.ToArray();
            if (bytes.Length == 0)
            {
                return null;
            }

            var text = System.Text.Encoding.UTF8.GetString(bytes);
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Trim();
            }

            var parsed = MakeMkv.LogParser.Parse(lines).ToList();
            if (parsed.Count == 0)
            {
                return null;
            }

            var organized = MakeMkv.LogParser.Organize(parsed);
            return organized.Titles?.ToList();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse engram scan log at {Path}; falling back to engram-title-only items", scanLogPath);
            return null;
        }
    }

    internal static void AddItemsFromParsedTitles(UserContributionDisc disc, List<MakeMkv.Title> parsedTitles, ICollection<EngramTitle> engramTitles)
    {
        foreach (var parsed in parsedTitles)
        {
            var hint = MatchEngramHint(parsed, engramTitles);
            if (hint == null)
            {
                // Engram only flagged a subset of the disc's titles as worth keeping.
                // Skip everything else so the user isn't presented with dozens of bonus
                // tracks/credits/etc. as already-identified items.
                continue;
            }

            disc.Items.Add(new UserContributionDiscItem
            {
                Name = !string.IsNullOrEmpty(hint.SourceFilename)
                    ? hint.SourceFilename!
                    : (parsed.Playlist ?? $"Title {parsed.Index}"),
                Source = parsed.Playlist ?? string.Empty,
                Duration = parsed.Length ?? string.Empty,
                Size = parsed.DisplaySize ?? string.Empty,
                ChapterCount = parsed.ChapterCount,
                SegmentCount = parsed.Segments?.Count(s => s.Type != null && s.Type.Equals("Video", StringComparison.OrdinalIgnoreCase)) ?? 0,
                SegmentMap = parsed.SegmentMap ?? string.Empty,
                Type = hint.TitleType ?? string.Empty,
                Season = hint.Season ?? string.Empty,
                Episode = hint.Episode ?? string.Empty,
                Description = string.Empty,
            });
        }
    }

    private static void AddItemsFromEngramTitles(UserContributionDisc disc, ICollection<EngramTitle> engramTitles)
    {
        foreach (var title in engramTitles.OrderBy(t => t.TitleIndex))
        {
            disc.Items.Add(new UserContributionDiscItem
            {
                Name = title.SourceFilename ?? $"Title {title.TitleIndex}",
                Source = title.SourceFilename ?? string.Empty,
                Duration = FormatDuration(title.DurationSeconds),
                Size = FormatSize(title.SizeBytes),
                ChapterCount = title.ChapterCount ?? 0,
                SegmentCount = title.SegmentCount ?? 0,
                SegmentMap = title.SegmentMap ?? string.Empty,
                Type = title.TitleType ?? string.Empty,
                Season = title.Season ?? string.Empty,
                Episode = title.Episode ?? string.Empty,
                Description = string.Empty,
            });
        }
    }

    /// <summary>
    /// Matches a parsed MakeMKV title to an Engram-supplied hint. Prefers SegmentMap
    /// (globally unique per disc) and falls back to title index.
    /// </summary>
    internal static EngramTitle? MatchEngramHint(MakeMkv.Title parsed, ICollection<EngramTitle> engramTitles)
    {
        if (engramTitles == null || engramTitles.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(parsed.SegmentMap))
        {
            var bySegment = engramTitles.FirstOrDefault(t => !string.IsNullOrEmpty(t.SegmentMap) && t.SegmentMap == parsed.SegmentMap);
            if (bySegment != null)
            {
                return bySegment;
            }
        }

        return engramTitles.FirstOrDefault(t => t.TitleIndex == parsed.Index);
    }

    public async ValueTask DisposeAsync()
    {
        this.cts.Cancel();
        this.cts.Dispose();

        if (database != null)
        {
            await database.DisposeAsync();
        }
    }
}

public class CreateFromEngramRequest
{
    [Required]
    public string MediaType { get; set; } = "movie";

    [Required(ErrorMessage = "Release Date is required")]
    public DateTimeOffset? ReleaseDate { get; set; }

    [Required(ErrorMessage = "ASIN is required")]
    [Asin]
    public string? Asin { get; set; }

    [Required(ErrorMessage = "UPC is required")]
    [Upc]
    public string? Upc { get; set; }

    [Required(ErrorMessage = "Locale is required")]
    public string? Locale { get; set; } = "en-US";

    [Required(ErrorMessage = "Region Code is required")]
    public string? RegionCode { get; set; } = "1";
}
