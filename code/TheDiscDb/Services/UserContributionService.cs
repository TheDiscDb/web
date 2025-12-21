using System.Text;
using System.Text.Json;
using Fantastic.TheMovieDb;
using Fantastic.TheMovieDb.Models;
using FluentResults;
using MakeMkv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Data.Import;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services.Server;

public class UserContributionService : IUserContributionService
{
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly UserManager<TheDiscDbUser> userManager;
    private readonly IPrincipalProvider principalProvider;
    private readonly IdEncoder idEncoder;
    private readonly IStaticAssetStore assetStore;
    private readonly IStaticAssetStore imageStore;
    private readonly TheMovieDbClient tmdb;
    private readonly IAmazonImporter amazon;

    public UserContributionService(IDbContextFactory<SqlServerDataContext> dbContextFactory, UserManager<TheDiscDbUser> userManager, IPrincipalProvider principalProvider, IdEncoder idEncoder, IStaticAssetStore assetStore, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore, TheMovieDbClient tmdb, IAmazonImporter amazon)
    {
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        this.principalProvider = principalProvider ?? throw new ArgumentNullException(nameof(principalProvider));
        this.idEncoder = idEncoder ?? throw new ArgumentNullException(nameof(idEncoder));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
        this.tmdb = tmdb ?? throw new ArgumentNullException(nameof(tmdb));
        this.amazon = amazon ?? throw new ArgumentNullException(nameof(amazon));
    }

    #region Contributions

    public async Task<FluentResults.Result<List<UserContribution>>> GetUserContributions(CancellationToken cancellationToken)
    {
        var user = this.principalProvider.Principal;
        if (user == null)
        {
            // TODO: Figure out how to return a 401 from here
            return Result.Fail("User not authenticated");
        }

        var userId = userManager.GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            // TODO: Figure out how to return a 401 from here
            return Result.Fail("User ID not found");
        }

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var contributions = await dbContext.UserContributions
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .OrderByDescending(c => c.Created)
                .ToListAsync(cancellationToken);

            this.idEncoder.EncodeInPlace(contributions);
            return contributions;
        }
    }

    public async Task<FluentResults.Result<CreateContributionResponse>> CreateContribution(string userId, CreateContributionRequest request, CancellationToken cancellationToken)
    {
        var contribution = new UserContribution
        {
            UserId = userId,
            Created = DateTimeOffset.UtcNow,
            Asin = request.Asin,
            ExternalId = request.ExternalId,
            ExternalProvider = request.ExternalProvider,
            MediaType = request.MediaType,
            ReleaseDate = request.ReleaseDate,
            Status = UserContributionStatus.Pending,
            FrontImageUrl = request.FrontImageUrl,
            BackImageUrl = request.BackImageUrl,
            Upc = request.Upc,
            ReleaseTitle = request.ReleaseTitle,
            ReleaseSlug = request.ReleaseSlug,
            Locale = request.Locale,
            RegionCode = request.RegionCode,
            Title = request.Title,
            Year = request.Year,
            TitleSlug = CreateSlug(request.Title, request.Year)
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            dbContext.UserContributions.Add(contribution);
            await dbContext.SaveChangesAsync(cancellationToken);

            this.idEncoder.EncodeInPlace(contribution);

            // Now that we have a contributionId, we can get the external data which will save it in blob storage
            if (string.IsNullOrEmpty(contribution.Title) || string.IsNullOrEmpty(contribution.Year))
            {
                var externalData = await this.GetExternalData(contribution.EncodedId, cancellationToken);
                if (externalData.IsSuccess)
                {
                    contribution.Title = externalData.Value.Title;
                    contribution.Year = externalData.Value.Year.ToString();
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            //Now move the uploaded assets from temp storage to the contribution folder
            await MoveImages(dbContext, contribution, request.FrontImageUrl ?? string.Empty, "front", (c, url) => c.FrontImageUrl = url, cancellationToken);
            await MoveImages(dbContext, contribution, request.BackImageUrl ?? string.Empty, "back", (c, url) => c.BackImageUrl = url, cancellationToken);
        }

        return new CreateContributionResponse { ContributionId = this.idEncoder.Encode(contribution.Id) };

        static string CreateSlug(string name, string year)
        {
            if (!string.IsNullOrEmpty(year))
            {
                return string.Format("{0}-{1}", name.Slugify(), year);
            }

            return name.Slugify();
        }
    }

    private async Task MoveImages(SqlServerDataContext dbContext, UserContribution contribution, string currentImageUrl, string name, Action<UserContribution, string> updateUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(currentImageUrl) && currentImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            // This shouldn't happen but just in case, we don't want to try and move an external URL
            return;
        }

        string remotePath = $"Contributions/releaseImages/{currentImageUrl}";
        if (await imageStore.Exists(remotePath, cancellationToken))
        {
            var data = await imageStore.Download(remotePath, cancellationToken);
            // Move the image to a folder based on the contribution id
            if (data != null)
            {
                var memoryStream = new MemoryStream(data.ToArray());

                string imageStoreRemotePath = $"Contributions/{contribution.EncodedId}/{name}.jpg";
                await imageStore.Save(memoryStream, imageStoreRemotePath, ContentTypes.ImageContentType, cancellationToken);

                memoryStream.Position = 0;
                string assetStoreRemotePath = $"{contribution.EncodedId}/{name}.jpg";
                await this.assetStore.Save(memoryStream, assetStoreRemotePath, ContentTypes.ImageContentType, cancellationToken);

                updateUrl(contribution, $"/images/Contributions/{contribution.EncodedId}/{name}.jpg");
                await dbContext.SaveChangesAsync(cancellationToken);

                // Delete from the old old location
                await imageStore.Delete(remotePath, cancellationToken);

                memoryStream.Dispose();
            }
        }
    }

    public async Task<FluentResults.Result<UserContribution>> GetContribution(string contributionId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .Include(c => c.HashItems)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            this.idEncoder.EncodeInPlace(contribution);
            return contribution;
        }
    }

    public async Task<Result> DeleteContribution(string contributionId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            dbContext.UserContributions.Remove(contribution);
            await dbContext.SaveChangesAsync(cancellationToken);

            // TODO: Delete images and blobs associated with this contribution
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateContribution(string contributionId, CreateContributionRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
            if (contribution == null)
            {
                return Result.Fail(contributionId + " not found");
            }

            contribution.Asin = request.Asin;
            contribution.ExternalId = request.ExternalId;
            contribution.ExternalProvider = request.ExternalProvider;
            contribution.MediaType = request.MediaType;
            contribution.ReleaseDate = request.ReleaseDate;
            contribution.Upc = request.Upc;
            contribution.ReleaseTitle = request.ReleaseTitle;
            contribution.ReleaseSlug = request.ReleaseSlug;
            contribution.Status = request.Status;

            // TODO: Handle images being updated before re-enabling this
            //contribution.FrontImageUrl = request.FrontImageUrl;
            //contribution.BackImageUrl = request.BackImageUrl;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<FluentResults.Result<HashDiscResponse>> HashDisc(string contributionId, HashDiscRequest request, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.HashItems)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            
            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            var hash = request.Files.OrderBy(f => f.Name).CalculateHash();
            var existingItems = contribution.HashItems?.Where(i => i.DiscHash == hash).ToList();
            foreach (var existing in existingItems ?? Enumerable.Empty<UserContributionDiscHashItem>())
            {
                contribution.HashItems!.Remove(existing);
                dbContext.UserContributionDiscHashItems.Remove(existing);
            }

            foreach (var item in request.Files)
            {
                contribution.HashItems!.Add(new UserContributionDiscHashItem
                {
                    DiscHash = hash,
                    CreationTime = item.CreationTime,
                    Index = item.Index,
                    Name = item.Name,
                    Size = item.Size
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var response = new HashDiscResponse
            {
                DiscHash = hash
            };

            return response;
        }
    }

    public async Task<FluentResults.Result<SeriesEpisodeNames>> GetEpisodeNames(string contributionId, CancellationToken cancellationToken = default)
    {
        // First check blob storage to see if the episode names file exists
        string filePath = $"{contributionId}/episode-names.json";
        bool exists = await this.assetStore.Exists(filePath, cancellationToken);
        if (exists)
        {
            var data = await this.assetStore.Download(filePath, cancellationToken);
            string json = Encoding.UTF8.GetString(data.ToArray());
            var results = JsonSerializer.Deserialize<SeriesEpisodeNames>(json);
            return results!;
        }

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            var series = await this.tmdb.GetSeries(contribution.ExternalId, cancellationToken: cancellationToken);
            if (series == null)
            {
                return Result.Fail($"Series with TMDB ID {contribution.ExternalId} not found");
            }

            var year = series.FirstAirDate.HasValue ? series.FirstAirDate.Value.Year : 0;

            var results = new SeriesEpisodeNames
            {
                SeriesTitle = series.Name ?? "Unknown Title",
                SeriesYear = year.ToString()
            };

            foreach (var season in series.Seasons)
            {
                var fullSeason = await this.tmdb.GetSeason(series.Id, season.SeasonNumber);
                foreach (var episode in fullSeason.Episodes)
                {
                    results.Episodes.Add(new SeriesEpisodeNameEntry
                    {
                        SeasonNumber = season.SeasonNumber.ToString(),
                        EpisodeNumber = episode.EpisodeNumber.ToString(),
                        EpisodeName = episode.Name ?? "Unknown Title"
                    });
                }
            }

            string json = JsonSerializer.Serialize(results, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Save to blob storage
            await this.assetStore.Save(new MemoryStream(Encoding.UTF8.GetBytes(json)), filePath, ContentTypes.JsonContentType, cancellationToken);
            return results!;
        }
    }

    public async Task<FluentResults.Result<ExternalMetadata>> GetExternalData(string contributionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            // First check blob storage to see if the episode names file exists
            string filePath = $"{contributionId}/tmdb.json";
            bool exists = await this.assetStore.Exists(filePath, cancellationToken);
            if (exists)
            {
                var data = await this.assetStore.Download(filePath, cancellationToken);
                string existingJson = Encoding.UTF8.GetString(data.ToArray());

                if (contribution.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
                {
                    var series = JsonSerializer.Deserialize<Series>(existingJson);
                    if (series == null)
                    {
                        return Result.Fail("Failed to deserialize series data from blob storage");
                    }

                    return new ExternalMetadata
                    {
                        Id = series.Id,
                        Title = series.Name ?? "Unknown Title",
                        Year = series.FirstAirDate.HasValue ? series.FirstAirDate.Value.Year : 0,
                        ImageUrl = "https://image.tmdb.org/t/p/w500" + series?.PosterPath
                    };
                }
                else
                {
                    var movie = JsonSerializer.Deserialize<Movie>(existingJson);
                    if (movie == null)
                    {
                        return Result.Fail("Failed to deserialize movie data from blob storage");
                    }

                    return new ExternalMetadata
                    {
                        Id = movie.Id,
                        Title = movie.Title ?? "Unknown Title",
                        Year = movie.ReleaseDate.HasValue ? movie.ReleaseDate.Value.Year : 0,
                        ImageUrl = "https://image.tmdb.org/t/p/w500" + movie?.PosterPath
                    };
                }
            }

            string? json = null;
            int? year = null;
            ExternalMetadata? metadata = null;

            if (contribution.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
            {
                var series = await this.tmdb.GetSeries(contribution.ExternalId, cancellationToken: cancellationToken);
                if (series == null)
                {
                    return Result.Fail($"Series with TMDB ID {contribution.ExternalId} not found");
                }

                year = series.FirstAirDate.HasValue ? series.FirstAirDate.Value.Year : 0;
                json = JsonSerializer.Serialize(series, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                metadata = new ExternalMetadata
                {
                    Id = series.Id,
                    Title = series.Name ?? "Unknown Title",
                    Year = year ?? 0,
                    ImageUrl = "https://image.tmdb.org/t/p/w500" + series?.PosterPath
                };

            }
            else
            {
                var movie = await this.tmdb.GetMovie(contribution.ExternalId, cancellationToken: cancellationToken);
                if (movie == null)
                {
                    return Result.Fail($"Movie with TMDB ID {contribution.ExternalId} not found");
                }

                year = movie.ReleaseDate.HasValue ? movie.ReleaseDate.Value.Year : 0;
                json = JsonSerializer.Serialize(movie, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                metadata = new ExternalMetadata
                {
                    Id = movie.Id,
                    Title = movie.Title ?? "Unknown Title",
                    Year = year ?? 0,
                    ImageUrl = "https://image.tmdb.org/t/p/w500" + movie?.PosterPath
                };
            }


            // Save to blob storage
            if (!string.IsNullOrEmpty(json))
            {
                await this.assetStore.Save(new MemoryStream(Encoding.UTF8.GetBytes(json)), filePath, ContentTypes.JsonContentType, cancellationToken);
            }

            return metadata;
        }
    }

    public async Task<FluentResults.Result<ExternalMetadata>> GetExternalData(string externalId, string mediaType, string provider, CancellationToken cancellationToken = default)
    {
        int? year = null;
        ExternalMetadata? metadata = null;
        if (mediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            var series = await this.tmdb.GetSeries(externalId, cancellationToken: cancellationToken);
            if (series == null)
            {
                return Result.Fail($"Series with TMDB ID {externalId} not found");
            }

            year = series.FirstAirDate.HasValue ? series.FirstAirDate.Value.Year : 0;

            metadata = new ExternalMetadata
            {
                Id = series.Id,
                Title = series.Name ?? "Unknown Title",
                Year = year ?? 0,
                ImageUrl = "https://image.tmdb.org/t/p/w500" + series?.PosterPath
            };

        }
        else
        {
            var movie = await this.tmdb.GetMovie(externalId, cancellationToken: cancellationToken);
            if (movie == null)
            {
                return Result.Fail($"Movie with TMDB ID {externalId} not found");
            }

            year = movie.ReleaseDate.HasValue ? movie.ReleaseDate.Value.Year : 0;

            metadata = new ExternalMetadata
            {
                Id = movie.Id,
                Title = movie.Title ?? "Unknown Title",
                Year = year ?? 0,
                ImageUrl = "https://image.tmdb.org/t/p/w500" + movie?.PosterPath
            };
        }

        return metadata;
    }

    public async Task<FluentResults.Result<ImportReleaseDetailsResponse>> ImportReleaseDetails(string asin, CancellationToken cancellationToken = default)
    {
        var result = await this.amazon.GetProductMetadataAsync(asin, cancellationToken);
        if (result == null)
        {
            return Result.Fail($"Amazon product with ASIN {asin} not found");
        }

        if (result.IsFailed)
        {
            return Result.Fail(result.Errors);
        }

        return new ImportReleaseDetailsResponse
        {
            Title = result.Value?.Title,
            ReleaseDate = result.Value?.ReleaseDate,
            Upc = result.Value?.Upc,
            FrontImageUrl = result.Value?.FrontImageUrl,
            BackImageUrl = result.Value?.BackImageUrl,
            MediaFormat = result.Value?.MediaFormat
        };
    }

    #endregion

    #region Discs

    public async Task<FluentResults.Result<List<UserContributionDisc>>> GetDiscs(string contributionId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (contribution == null)
            {
                throw new Exception("Contribution not found");
            }

            this.idEncoder.EncodeInPlace(contribution);
            return contribution.Discs.ToList();
        }
    }

    public async Task<FluentResults.Result<UserContributionDisc>> GetDisc(string contributionId, string discId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            this.idEncoder.EncodeInPlace(disc);
            return disc;
        }
    }

    public async Task<Result> SaveDiscLogs(string contributionId, string discId, string logs, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            // Convert any LF line endings to CRLF
            logs = logs.Replace("\r\n", "\n") // normalize any CRLF to LF first
                .Replace("\n", "\r\n"); // then convert LF to CRLF

            byte[] byteArray = Encoding.UTF8.GetBytes(logs);
            using (MemoryStream memoryStream = new MemoryStream(byteArray))
            {
                // Validate the logs are from makemkv and not something else
                memoryStream.Position = 0;
                List<string> allLines = new();
                using (StreamReader reader = new StreamReader(memoryStream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        allLines.Add(line);
                    }

                    try
                    {
                        _ = LogParser.Parse(allLines);
                        
                    }
                    catch (Exception)
                    {
                        return Result.Fail($"Could not parse log file");
                    }
                }

                // TODO: if the logs have changed, rewrite the memorystream

                //Save the logs in blob storage
                memoryStream.Position = 0;
                await this.assetStore.Save(memoryStream, $"{contributionId}/{this.idEncoder.Encode(disc.Id)}-logs.txt", ContentTypes.TextContentType, cancellationToken);
            }

            disc.LogsUploaded = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();

        //TODO: Notify the client a disc has been added? (to prevent the client having to poll)
    }

    public async Task<FluentResults.Result<DiscLogResponse>> GetDiscLogs(string contributionId, string discId, CancellationToken cancellationToken)
    {
        // TODO: Check the user owns the contribution

        try
        {
            var blob = await this.assetStore.Download($"{contributionId}/{discId}-logs.txt", cancellationToken);
            if (blob == null)
            {
                // TODO: Try getting the status code in a middleware and changing the response code
                return Result.Fail(new FluentResults.Error("Logs not found").WithMetadata("StatusCode", StatusCodes.Status404NotFound));
            }

            string text = blob.ToString();
            var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var parsed = LogParser.Parse(lines);
            var orgainized = LogParser.Organize(parsed);

            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var decodedDiscId = this.idEncoder.Decode(discId);
            UserContributionDisc? disc = null;
            UserContribution? contribution = null;
            await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
            {
                contribution = await dbContext.UserContributions
                    .Include(c => c.Discs)
                    .ThenInclude(c => c.Items)
                        .ThenInclude(d => d.Chapters)
                    .Include(c => c.Discs)
                    .ThenInclude(c => c.Items)
                        .ThenInclude(d => d.AudioTracks)
                    .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

                if (contribution == null)
                {
                    return Result.Fail(new FluentResults.Error($"Contribution {contributionId} not found"));
                }

                disc = contribution.Discs.FirstOrDefault(d => d.Id == decodedDiscId);
                if (disc == null)
                {
                    return Result.Fail(new FluentResults.Error($"Disc {discId} not found"));
                }
            }

            this.idEncoder.EncodeInPlace(disc);
            this.idEncoder.EncodeInPlace(contribution);
            return Result.Ok(new DiscLogResponse
            {
                Info = orgainized,
                Disc = disc,
                Contribution = contribution
            });
        }
        catch (Exception e)
        {
            return Result.Fail(new FluentResults.Error("Error getting disc logs").CausedBy(e));
        }
    }

    public async Task<FluentResults.Result<SaveDiscResponse>> CreateDisc(string contributionId, SaveDiscRequest request, CancellationToken cancellationToken)
    {
        var disc = new UserContributionDisc
        {
            ContentHash = request.ContentHash,
            Format = request.Format,
            Name = request.Name,
            Slug = request.Slug,
            ExistingDiscPath = request.ExistingDiscPath
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            var existingDisc = contribution?.Discs.FirstOrDefault(d => d.ContentHash == disc.ContentHash);
            if (existingDisc != null)
            {
                existingDisc.Format = request.Format;
                existingDisc.Name = request.Name;
                existingDisc.Slug = request.Slug;
                existingDisc.ExistingDiscPath = request.ExistingDiscPath;
                await dbContext.SaveChangesAsync(cancellationToken);
                return new SaveDiscResponse { DiscId = this.idEncoder.Encode(existingDisc.Id) };
            }
            else
            {
                contribution!.Discs.Add(disc);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SaveDiscResponse { DiscId = this.idEncoder.Encode(disc.Id) };
    }

    public async Task<Result> UpdateDisc(string contributionId, string discId, SaveDiscRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);
            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            disc.ContentHash = request.ContentHash;
            disc.Format = request.Format;
            disc.Name = request.Name;
            disc.Slug = request.Slug;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteDisc(string contributionId, string discId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            contribution.Discs.Remove(disc);
            dbContext.UserContributionDiscs.Remove(disc);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<FluentResults.Result<DiscStatusResponse>> CheckDiskUploadStatus(string discId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            if (string.IsNullOrEmpty(discId))
            {
                return Result.Fail("Disc ID is required");
            }

            int realDiscId = this.idEncoder.Decode(discId);

            if (realDiscId == 0)
            {
                return Result.Fail("Invalid disc ID");
            }

            var disc = await dbContext.UserContributionDiscs
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == realDiscId, cancellationToken);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            return new DiscStatusResponse
            {
                LogsUploaded = disc.LogsUploaded
            };
        }
    }

    #endregion

    #region Disc Items

    public async Task<FluentResults.Result<AddItemResponse>> AddItemToDisc(string contributionId, string discId, AddItemRequest request, CancellationToken cancellationToken)
    {
        var item = new UserContributionDiscItem
        {
            ChapterCount = request.ChapterCount,
            Description = request.Description,
            Duration = request.Duration,
            Size = request.Size,
            Name = request.Name,
            SegmentCount = request.SegmentCount,
            SegmentMap = request.SegmentMap,
            Source = request.Source,
            Type = request.Type,
            Season = request.Season,
            Episode = request.Episode
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            disc.Items.Add(item);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AddItemResponse { ItemId = this.idEncoder.Encode(item.Id) };
    }

    public async Task<Result> EditItemOnDisc(string contributionId, string discId, string itemId, EditItemRequest request, CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var decodedDiscId = this.idEncoder.Decode(discId);
        var decodedItemId = this.idEncoder.Decode(itemId);

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs.Where(d => d.Id == decodedDiscId))
                    .ThenInclude(d => d.Items.Where(i => i.Id == decodedItemId))
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            var disc = contribution.Discs.FirstOrDefault();
            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            var item = disc.Items.FirstOrDefault();
            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            item.ChapterCount = request.ChapterCount;
            item.Description = request.Description;
            item.Duration = request.Duration;
            item.Size = request.Size;
            item.Name = request.Name;
            item.SegmentCount = request.SegmentCount;
            item.SegmentMap = request.SegmentMap;
            item.Source = request.Source;
            item.Type = request.Type;
            item.Season = request.Season;
            item.Episode = request.Episode;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteItemFromDisc(string contributionId, string discId, string itemId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = this.idEncoder.Decode(itemId);
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            disc.Items.Remove(item);
            dbContext.UserContributionDiscItems.Remove(item);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return Result.Ok();
    }

    #endregion

    #region Chapters

    public async Task<FluentResults.Result<AddChapterResponse>> AddChapterToItem(string contributionId, string discId, string itemId, AddChapterRequest request, CancellationToken cancellationToken)
    {
        var chapter = new UserContributionChapter
        {
            Index = request.Index,
            Title = request.Title
        };
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);
            
            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = this.idEncoder.Decode(itemId);
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);
            
            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            var existingChapter = item.Chapters.FirstOrDefault(c => c.Index == chapter.Index);
            if (existingChapter != null)
            {
                existingChapter.Title = chapter.Title;
            }
            else
            {
                item.Chapters.Add(chapter);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AddChapterResponse { ChapterId = this.idEncoder.Encode(chapter.Id) };
    }

    public async Task<Result> DeleteChapterFromItem(string contributionId, string discId, string itemId, string chapterId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);
            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }
            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = this.idEncoder.Decode(itemId);
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            int realChapterId = this.idEncoder.Decode(chapterId);
            var chapter = item.Chapters.FirstOrDefault(c => c.Id == realChapterId);

            if (chapter == null)
            {
                return Result.Fail($"Chapter {chapterId} not found");
            }

            item.Chapters.Remove(chapter);
            dbContext.UserContributionChapters.Remove(chapter);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateChapterInItem(string contributionId, string discId, string itemId, string chapterId, AddChapterRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.Chapters)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = this.idEncoder.Decode(itemId);
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            int realChapterId = this.idEncoder.Decode(chapterId);
            var chapter = item.Chapters.FirstOrDefault(c => c.Id == realChapterId);

            if (chapter == null)
            {
                return Result.Fail($"Chapter {chapterId} not found");
            }

            chapter.Index = request.Index;
            chapter.Title = request.Title;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    #endregion

    #region Audio Tracks

    public async Task<FluentResults.Result<AddAudioTrackResponse>> AddAudioTrackToItem(string contributionId, string discId, string itemId, AddAudioTrackRequest request, CancellationToken cancellationToken)
    {
        var audioTrack = new UserContributionAudioTrack
        {
            Index = request.Index,
            Title = request.Title
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = this.idEncoder.Decode(itemId);
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            var existingTrack = item.AudioTracks.FirstOrDefault(c => c.Index == audioTrack.Index);
            if (existingTrack != null)
            {
                existingTrack.Title = audioTrack.Title;
            }
            else
            {
                item.AudioTracks.Add(audioTrack);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AddAudioTrackResponse { AudioTrackId = this.idEncoder.Encode(audioTrack.Id) };
    }

    public async Task<Result> DeleteAudioTrackFromItem(string contributionId, string discId, string itemId, string audioTrackId, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = this.idEncoder.Decode(itemId);
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            int realAudioTrackId = this.idEncoder.Decode(audioTrackId);
            var audioTrack = item.AudioTracks.FirstOrDefault(a => a.Id == realAudioTrackId);

            if (audioTrack == null)
            {
                return Result.Fail($"Audio track {audioTrackId} not found");
            }

            item.AudioTracks.Remove(audioTrack);
            dbContext.UserContributionAudioTracks.Remove(audioTrack);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateAudioTrackInItem(string contributionId, string discId, string itemId, string audioTrackId, AddAudioTrackRequest request, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var decodedContributionId = this.idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                    .ThenInclude(d => d.Items)
                        .ThenInclude(i => i.AudioTracks)
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = this.idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            int realItemId = this.idEncoder.Decode(itemId);
            var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

            if (item == null)
            {
                return Result.Fail($"Item {itemId} not found");
            }

            int realAudioTrackId = this.idEncoder.Decode(audioTrackId);
            var audioTrack = item.AudioTracks.FirstOrDefault(a => a.Id == realAudioTrackId);

            if (audioTrack == null)
            {
                return Result.Fail($"Audio track {audioTrackId} not found");
            }

            audioTrack.Index = request.Index;
            audioTrack.Title = request.Title;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    #endregion
}