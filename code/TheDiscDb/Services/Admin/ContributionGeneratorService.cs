using System.Text;
using System.Text.Json;
using Fantastic.FileSystem;
using Fantastic.TheMovieDb;
using MakeMkv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb.Client;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Data.Import;
using TheDiscDb.Import;
using TheDiscDb.ImportModels;
using TheDiscDb.InputModels;
using TheDiscDb.Services.Admin.Workspace;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services.Admin;

public class ContributionGeneratorService
{
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly IFileSystem fileSystem;
    private readonly HttpClient httpClient;
    private readonly TheMovieDbClient tmdb;
    private readonly IStaticAssetStore contributionsAssetStore;
    private readonly IStaticAssetStore imageStore;
    private readonly SqidsEncoder<int> idEncoder;
    private readonly UserManager<TheDiscDbUser> userManager;

    public ContributionGeneratorService(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IFileSystem fileSystem,
        HttpClient httpClient,
        TheMovieDbClient tmdb,
        IStaticAssetStore contributionsAssetStore,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        SqidsEncoder<int> idEncoder,
        UserManager<TheDiscDbUser> userManager)
    {
        this.dbContextFactory = dbContextFactory;
        this.fileSystem = fileSystem;
        this.httpClient = httpClient;
        this.tmdb = tmdb;
        this.contributionsAssetStore = contributionsAssetStore;
        this.imageStore = imageStore;
        this.idEncoder = idEncoder;
        this.userManager = userManager;
    }

    /// <summary>
    /// Generates all contribution files into the workspace. Returns the release folder path and
    /// the files that were written so git can stage only the touched paths.
    /// </summary>
    public async Task<ContributionGenerationResult> GenerateAsync(
        int contributionId,
        bool overwrite,
        IDataRepositoryWorkspace workspace,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);

        var contribution = await dbContext.UserContributions
            .AsNoTracking()
            .Include(c => c.Discs)
            .Include(c => c.HashItems)
            .FirstOrDefaultAsync(c => c.Id == contributionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contribution {contributionId} not found.");

        log($"Generating release for contribution {this.idEncoder.Encode(contribution.Id)} - {contribution.ReleaseTitle}");

        return await GenerateContribution(contribution, overwrite, null, null, dbContext, workspace, log, cancellationToken);
    }

    internal async Task<ContributionGenerationResult> GenerateContribution(
        UserContribution contribution,
        bool overwrite,
        string[]? categories,
        string[]? releaseCategories,
        SqlServerDataContext dbContext,
        IDataRepositoryWorkspace workspace,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        string externalId = GetTmdbExternalId(contribution);

        ImportItemType itemType = contribution.MediaType.ToLower() switch
        {
            "movie" => ImportItemType.Movie,
            "series" => ImportItemType.Series,
            "boxset" => ImportItemType.Boxset,
            _ => throw new InvalidOperationException($"Unknown media type: {contribution.MediaType}"),
        };

        var importItem = new ImportItem { Type = itemType };

        if (itemType == ImportItemType.Series)
        {
            importItem.Series = await this.tmdb.GetSeries(externalId, cancellationToken: cancellationToken);
        }
        else
        {
            importItem.Movie = await this.tmdb.GetMovie(externalId, cancellationToken: cancellationToken);
        }

        int year = importItem.TryGetYear();
        MetadataFile? metadata = ImportHelper.BuildMetadata(
            importItem.ImdbTitle,
            importItem.GetTmdbItemToSerialize() as Fantastic.TheMovieDb.Models.Movie,
            importItem.GetTmdbItemToSerialize() as Fantastic.TheMovieDb.Models.Series,
            year,
            itemType);

        string folderName = BuildContributionFolderName(contribution, metadata, year);
        string subFolderName = itemType == ImportItemType.Series ? "series" : "movie";

        string basePath = this.fileSystem.Path.Combine(workspace.DataRepositoryPath, subFolderName, folderName);
        log($"Importing into {basePath}");
        var generatedFiles = new HashSet<string>(StringComparer.Ordinal);

        if (!await this.fileSystem.Directory.Exists(basePath))
        {
            await this.fileSystem.Directory.CreateDirectory(basePath);
        }

        string? posterUrl = importItem.GetPosterUrl();
        string posterPath = this.fileSystem.Path.Combine(basePath, "cover.jpg");
        if (!await this.fileSystem.File.Exists(posterPath))
        {
            if (!string.IsNullOrEmpty(posterUrl))
            {
                log("Downloading poster...");
                await this.httpClient.Download(this.fileSystem, posterUrl, posterPath);
                generatedFiles.Add(posterPath);
            }
        }

        string tmdbPath = this.fileSystem.Path.Combine(basePath, "tmdb.json");
        if (!await this.fileSystem.File.Exists(tmdbPath))
        {
            if (importItem.GetTmdbItemToSerialize() != null)
            {
                await this.fileSystem.File.WriteAllText(tmdbPath, JsonSerializer.Serialize(importItem.GetTmdbItemToSerialize(), JsonHelper.JsonOptions));
                generatedFiles.Add(tmdbPath);
            }
        }

        string imdbPath = this.fileSystem.Path.Combine(basePath, "imdb.json");
        if (!await this.fileSystem.File.Exists(imdbPath))
        {
            if (importItem.ImdbTitle != null)
            {
                await this.fileSystem.File.WriteAllText(imdbPath, JsonSerializer.Serialize(importItem.ImdbTitle, JsonHelper.JsonOptions));
                generatedFiles.Add(imdbPath);
            }
        }

        string metadataPath = this.fileSystem.Path.Combine(basePath, MetadataFile.Filename);
        if (!await this.fileSystem.File.Exists(metadataPath))
        {
            if (categories is { Length: > 0 })
            {
                foreach (var cat in categories)
                {
                    foreach (var name in cat.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!metadata!.Groups.Contains(name))
                        {
                            metadata.Groups.Add(name);
                        }
                    }
                }
            }

            await this.fileSystem.File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonHelper.JsonOptions));
            generatedFiles.Add(metadataPath);
        }

        string releaseFolder = this.fileSystem.Path.Combine(basePath, contribution.ReleaseSlug ?? string.Empty);

        bool justCreatedReleaseFolder = false;
        if (!await this.fileSystem.Directory.Exists(releaseFolder))
        {
            await this.fileSystem.Directory.CreateDirectory(releaseFolder);
            justCreatedReleaseFolder = true;
        }

        if (justCreatedReleaseFolder || overwrite)
        {
            await DownloadReleaseImages(contribution, releaseFolder, overwrite, generatedFiles, log, cancellationToken);

            string releaseFile = this.fileSystem.Path.Combine(releaseFolder, ReleaseFile.Filename);
            if (!await this.fileSystem.File.Exists(releaseFile) || overwrite)
            {
                var release = new ReleaseFile
                {
                    Title = contribution.ReleaseTitle,
                    SortTitle = $"{contribution.ReleaseDate.Year} {ImportHelper.GetSortTitle(contribution.ReleaseTitle)}",
                    Slug = contribution.ReleaseSlug,
                    Upc = contribution.Upc,
                    Locale = contribution.Locale ?? "en-us",
                    Year = contribution.ReleaseDate.Year,
                    RegionCode = contribution.RegionCode ?? "1",
                    Asin = contribution.Asin,
                    ReleaseDate = contribution.ReleaseDate,
                    DateAdded = DateTime.UtcNow.Date
                };

                var user = await this.userManager.FindByIdAsync(contribution.UserId);
                if (user != null)
                {
                    var contributor = await dbContext.Contributors
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.UserId == user.Id || c.Name == user.UserName, cancellationToken);

                    if (contributor != null)
                    {
                        release.Contributors.Add(new TheDiscDb.ImportModels.Contributor(contributor.Name!, contributor.Source!));
                    }
                    else
                    {
                        release.Contributors.Add(new TheDiscDb.ImportModels.Contributor(user.UserName!, "thediscdb"));
                    }
                }

                if (releaseCategories is { Length: > 0 })
                {
                    foreach (var cat in releaseCategories)
                    {
                        foreach (var name in cat.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (!release.Groups.Contains(name))
                            {
                                release.Groups.Add(name);
                            }
                        }
                    }
                }

                await this.fileSystem.File.WriteAllText(releaseFile, JsonSerializer.Serialize(release, JsonHelper.JsonOptions));
                generatedFiles.Add(releaseFile);
            }
        }

        int index = 1;
        foreach (var disc in contribution.Discs.OrderBy(d => d.Index ?? int.MaxValue).ThenBy(d => d.Id))
        {
            var discName = new DiscName { Index = index, Name = string.Format("disc{0:00}", index) };

            string resolution = ContributionDiscFormat.ResolveResolution(disc.Format);
            string discFormat = disc.Format;
            if (disc.Format.Equals(DiscFormatConstants.FourK, StringComparison.OrdinalIgnoreCase))
            {
                discFormat = DiscFormatConstants.Uhd;
            }

            if (!string.IsNullOrEmpty(disc.ExistingDiscPath))
            {
                await CopyExistingDisc(disc, discName, basePath, releaseFolder, subFolderName, workspace, contribution, overwrite, generatedFiles, log, cancellationToken);
            }
            else
            {
                await GenerateDiscFiles(contribution, disc, discName, releaseFolder, resolution, discFormat, year, overwrite, generatedFiles, dbContext, log, cancellationToken);
            }

            ++index;
        }

        log($"Generated {generatedFiles.Count} file(s) in workspace.");
        return new ContributionGenerationResult(releaseFolder, generatedFiles.ToList());
    }

    private async Task DownloadReleaseImages(
        UserContribution contribution,
        string releaseFolder,
        bool overwrite,
        ICollection<string> generatedFiles,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(contribution.FrontImageUrl))
        {
            string frontCoverPath = this.fileSystem.Path.Combine(releaseFolder, "front.jpg");
            if (!await this.fileSystem.File.Exists(frontCoverPath) || overwrite)
            {
                if (await DownloadContributionImage(contribution.FrontImageUrl, frontCoverPath, "Front", log, cancellationToken))
                {
                    generatedFiles.Add(frontCoverPath);
                }
            }
        }

        if (!string.IsNullOrEmpty(contribution.BackImageUrl))
        {
            string backCoverPath = this.fileSystem.Path.Combine(releaseFolder, "back.jpg");
            if (!await this.fileSystem.File.Exists(backCoverPath) || overwrite)
            {
                if (await DownloadContributionImage(contribution.BackImageUrl, backCoverPath, "Back", log, cancellationToken))
                {
                    generatedFiles.Add(backCoverPath);
                }
            }
        }
    }

    private async Task<bool> DownloadContributionImage(
        string imageUrl,
        string destPath,
        string label,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (imageUrl.StartsWith("http"))
        {
            await this.httpClient.Download(this.fileSystem, imageUrl, destPath);
            return true;
        }

        if (imageUrl.StartsWith("/images/Contributions/", StringComparison.OrdinalIgnoreCase))
        {
            string remotePath = imageUrl.Substring("/images/".Length);
            if (await this.imageStore.Exists(remotePath, cancellationToken))
            {
                var data = await this.imageStore.Download(remotePath, cancellationToken);
                using var stream = await this.fileSystem.File.OpenWrite(destPath, cancellationToken);
                stream.Write(data.ToArray());
                return true;
            }

            log($"Warning: {label} image '{imageUrl}' not found in asset store.");
        }

        return false;
    }

    private string BuildContributionFolderName(UserContribution contribution, MetadataFile? metadata, int year)
    {
        string? mediaTitle = metadata?.Title;
        if (string.IsNullOrWhiteSpace(mediaTitle))
        {
            mediaTitle = contribution.Title;
        }

        if (string.IsNullOrWhiteSpace(mediaTitle))
        {
            mediaTitle = contribution.ReleaseTitle;
        }

        if (string.IsNullOrWhiteSpace(mediaTitle))
        {
            string encodedContributionId = this.idEncoder.Encode(contribution.Id);
            throw new InvalidOperationException($"Unable to determine media title for contribution '{encodedContributionId}'.");
        }

        return $"{this.fileSystem.CleanPath(mediaTitle)} ({year})";
    }

    private string GetTmdbExternalId(UserContribution contribution)
    {
        if (!string.IsNullOrWhiteSpace(contribution.ExternalProvider)
            && !string.Equals(contribution.ExternalProvider, "tmdb", StringComparison.OrdinalIgnoreCase))
        {
            string encodedContributionId = this.idEncoder.Encode(contribution.Id);
            throw new InvalidOperationException(
                $"Contribution '{encodedContributionId}' has unsupported external provider '{contribution.ExternalProvider}'. TMDB is required; update ExternalProvider to 'TMDB' and set ExternalId to the TMDB id before generating.");
        }

        if (string.IsNullOrWhiteSpace(contribution.ExternalId))
        {
            string encodedContributionId = this.idEncoder.Encode(contribution.Id);
            throw new InvalidOperationException(
                $"Contribution '{encodedContributionId}' has no external id. A TMDB id is required before generating.");
        }

        return contribution.ExternalId;
    }

    private async Task CopyExistingDisc(
        UserContributionDisc disc,
        DiscName discName,
        string basePath,
        string releaseFolder,
        string subFolderName,
        IDataRepositoryWorkspace workspace,
        UserContribution contribution,
        bool overwrite,
        ICollection<string> generatedFiles,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log($"Resolving copied disc source '{disc.ExistingDiscPath}' for contribution disc '{discName.Name}' (hash: {disc.ContentHash}).");
        var existingDisc = UserContributionDisc.ParseDiscPath(disc.ExistingDiscPath!);
        string lookupBasePath = this.fileSystem.Path.Combine(workspace.DataRepositoryPath, subFolderName);

        string existingReleasePath = this.fileSystem.Path.Combine(basePath, existingDisc.ReleaseSlug);
        if (existingDisc.ExternalId != contribution.ExternalId)
        {
            var (existingTitle, existingYear, _) = await GetItemMetadata(existingDisc.ExternalId, existingDisc.MediaType);
            existingReleasePath = this.fileSystem.Path.Combine(lookupBasePath, $"{this.fileSystem.CleanPath(existingTitle)} ({existingYear})", existingDisc.ReleaseSlug);
        }

        if (!await this.fileSystem.Directory.Exists(existingReleasePath))
        {
            throw new InvalidOperationException($"Copied disc source release path was not found: '{existingReleasePath}' (from '{disc.ExistingDiscPath}').");
        }

        var discFiles = await this.fileSystem.Directory.GetFiles(existingReleasePath, "disc*.json", cancellationToken);
        if (!discFiles.Any())
        {
            throw new InvalidOperationException($"Copied disc source release '{existingReleasePath}' contains no disc*.json files (from '{disc.ExistingDiscPath}').");
        }

        var matchedDisc = false;
        foreach (var existingDiscFile in discFiles)
        {
            string discFileContents = await this.fileSystem.File.ReadAllText(existingDiscFile, cancellationToken);
            var discJson = JsonSerializer.Deserialize<Disc>(discFileContents, JsonHelper.JsonOptions);
            if (discJson != null && discJson.Slug == existingDisc.DiscSlug)
            {
                matchedDisc = true;
                string newDiscFilePath = this.fileSystem.Path.Combine(releaseFolder, $"{discName.Name}.json");
                await this.fileSystem.File.Copy(existingDiscFile, newDiscFilePath, overwrite, cancellationToken: cancellationToken);
                generatedFiles.Add(newDiscFilePath);

                string existingDir = this.fileSystem.Path.GetDirectoryName(existingDiscFile)!;
                string newSummaryFilePath = this.fileSystem.Path.Combine(releaseFolder, $"{discName.Name}-summary.txt");
                var sourceDiscStem = this.fileSystem.Path.GetFileNameWithoutExtension(existingDiscFile);
                string existingSummaryFilePath = this.fileSystem.Path.Combine(existingDir, $"{sourceDiscStem}-summary.txt");
                if (!await this.fileSystem.File.Exists(existingSummaryFilePath, cancellationToken))
                {
                    throw new InvalidOperationException($"Copied disc source summary file was not found: '{existingSummaryFilePath}' (from '{disc.ExistingDiscPath}').");
                }
                await this.fileSystem.File.Copy(existingSummaryFilePath, newSummaryFilePath, overwrite, cancellationToken: cancellationToken);
                generatedFiles.Add(newSummaryFilePath);

                string newLogFilePath = this.fileSystem.Path.Combine(releaseFolder, $"{discName.Name}.txt");
                string existingLogFilePath = this.fileSystem.Path.Combine(existingDir, $"{sourceDiscStem}.txt");
                if (!await this.fileSystem.File.Exists(existingLogFilePath, cancellationToken))
                {
                    throw new InvalidOperationException($"Copied disc source log file was not found: '{existingLogFilePath}' (from '{disc.ExistingDiscPath}').");
                }
                await this.fileSystem.File.Copy(existingLogFilePath, newLogFilePath, overwrite, cancellationToken: cancellationToken);
                generatedFiles.Add(newLogFilePath);

                log($"Copied disc source '{disc.ExistingDiscPath}' into '{discName.Name}' using source stem '{sourceDiscStem}' (hash: {disc.ContentHash}).");
                break;
            }
        }

        if (!matchedDisc)
        {
            throw new InvalidOperationException($"Copied disc source slug '{existingDisc.DiscSlug}' was not found in '{existingReleasePath}' (from '{disc.ExistingDiscPath}').");
        }
    }

    private async Task GenerateDiscFiles(
        UserContribution contribution,
        UserContributionDisc disc,
        DiscName discName,
        string releaseFolder,
        string resolution,
        string discFormat,
        int year,
        bool overwrite,
        ICollection<string> generatedFiles,
        SqlServerDataContext dbContext,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        string summaryFilePath = this.fileSystem.Path.Combine(releaseFolder, $"{discName.Name}-summary.txt");
        string makeMkvLogPath = this.fileSystem.Path.Combine(releaseFolder, $"{discName.Name}.txt");

        string discLogBlobPath = $"{this.idEncoder.Encode(contribution.Id)}/{this.idEncoder.Encode(disc.Id)}-logs.txt";

        if (!await this.fileSystem.File.Exists(makeMkvLogPath) || overwrite)
        {
            MakeMkv.DiscInfo? discInfo = null;
            if (await this.contributionsAssetStore.Exists(discLogBlobPath, cancellationToken))
            {
                var data = await this.contributionsAssetStore.Download(discLogBlobPath, cancellationToken);
                string logs = Encoding.UTF8.GetString(data.ToArray());
                await this.fileSystem.File.WriteAllText(makeMkvLogPath, logs);
                await LogParser.CleanInPlace(makeMkvLogPath);
                generatedFiles.Add(makeMkvLogPath);
                discInfo = LogParser.Organize(makeMkvLogPath);

                if (contribution.HashItems != null && contribution.HashItems.Count > 0)
                {
                    var hashInfo = new DiscHashInfo();
                    foreach (var item in contribution.HashItems.Where(h => h.DiscHash == disc.ContentHash))
                    {
                        hashInfo.Files.Add(new FileHashInfo
                        {
                            Index = item.Index,
                            Name = item.Name,
                            CreationTime = item.CreationTime,
                            Size = item.Size
                        });
                    }

                    await this.TryAppendHashInfo(makeMkvLogPath, hashInfo, generatedFiles, cancellationToken);
                }
            }

            var discItems = await dbContext.UserContributionDiscItems
                .AsNoTracking()
                .Include(i => i.Chapters)
                .Include(i => i.AudioTracks)
                .Where(i => i.Disc.Id == disc.Id)
                .ToListAsync(cancellationToken);

            var summaryItems = discItems.Select(i => CreateSummaryFileItem(i));
            var summaryFileMemoryStream = new MemoryStream();
            await SummaryFileSerializer.SerializeAsync(summaryFileMemoryStream, summaryItems, this.fileSystem, new SummaryFileMetadata(year, resolution));

            using (var stream = await this.fileSystem.File.OpenWrite(summaryFilePath, cancellationToken))
            {
                summaryFileMemoryStream.Position = 0;
                await summaryFileMemoryStream.CopyToAsync(stream, cancellationToken);
            }
            generatedFiles.Add(summaryFilePath);

            string discJsonFilePath = this.fileSystem.Path.Combine(releaseFolder, $"{discName.Name}.json");

            var discJsonFile = new Disc
            {
                Index = discName.Index,
                Slug = disc.Slug,
                Name = disc.Name,
                Format = discFormat,
                ContentHash = disc.ContentHash
            };

            summaryFileMemoryStream.Position = 0;
            string summaryContents = await new StreamReader(summaryFileMemoryStream).ReadToEndAsync(cancellationToken);
            var discFile = SummaryFileParser.ParseSingleDisc(summaryContents);

            if (discInfo != null)
            {
                DiscFileFinalizer.Map(discJsonFile, discFile, discInfo);
            }

            await this.fileSystem.File.WriteAllText(discJsonFilePath, JsonSerializer.Serialize(discJsonFile, JsonHelper.JsonOptions));
            generatedFiles.Add(discJsonFilePath);
            log($"Generated disc files: {discName.Name}");
        }
    }

    private async Task TryAppendHashInfo(string logFile, DiscHashInfo hashInfo, ICollection<string> generatedFiles, CancellationToken cancellationToken)
    {
        if (!await this.fileSystem.File.Exists(logFile))
        {
            return;
        }

        var lines = File.ReadAllLines(logFile);
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (!line.StartsWith(HashInfoLogLine.LinePrefix, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(line);
            }
        }

        foreach (var info in hashInfo.Files)
        {
            var logLine = new HashInfoLogLine
            {
                Index = info.Index,
                Name = info.Name,
                CreationTime = info.CreationTime,
                Size = info.Size
            };
            result.Add(logLine.ToString());
        }

        await this.fileSystem.File.WriteAllLines(logFile, result, cancellationToken);
        generatedFiles.Add(logFile);
    }

    private async Task<(string Title, string Year, string Slug)> GetItemMetadata(string externalId, string mediaType)
    {
        if (mediaType.Equals("movie", StringComparison.OrdinalIgnoreCase))
        {
            var movie = await this.tmdb.GetMovie(externalId);
            string title = movie.Title ?? $"{mediaType} {externalId}";
            int year = movie.ReleaseDate?.Year ?? 0;
            string slug = ImportHelper.CreateSlug(title, year);
            return (title, year.ToString(), slug);
        }
        else if (mediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            var series = await this.tmdb.GetSeries(externalId);
            string title = series.Name ?? $"{mediaType} {externalId}";
            int year = series.FirstAirDate?.Year ?? 0;
            string slug = ImportHelper.CreateSlug(title, year);
            return (title, year.ToString(), slug);
        }

        throw new InvalidOperationException($"Unknown media type: {mediaType}");
    }

    private static SummaryFileItem CreateSummaryFileItem(UserContributionDiscItem item)
    {
        return new SummaryFileItem
        {
            Name = item.Name,
            SourceFileName = item.Source,
            Duration = item.Duration,
            ChapterCount = item.ChapterCount.ToString(),
            Size = item.Size,
            SegmentCount = item.SegmentCount.ToString(),
            SegmentMap = item.SegmentMap,
            Type = item.Type,
            Chapters = item.Chapters.Select(c => new SummaryFileChildItem { Index = c.Index, Name = c.Title }).ToList(),
            AudioTracks = item.AudioTracks.Select(a => new SummaryFileChildItem { Index = a.Index, Name = a.Title }).ToList(),
            Description = item.Description ?? string.Empty,
            Episode = item.Episode ?? string.Empty,
            Season = item.Season ?? string.Empty
        };
    }
}
