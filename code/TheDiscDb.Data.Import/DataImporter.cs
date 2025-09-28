namespace TheDiscDb.Data.Import
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Fantastic.FileSystem;
    using IMDbApiLib.Models;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Options;
    using Spectre.Console;
    using TheDiscDb.ImportModels;
    using TheDiscDb.InputModels;
    using TheDiscDb.Web.Data;

    public class DataImporter
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IFileSystem fileSystem;
        private readonly SqlServerDataContext dbContext;
        private readonly IStaticAssetStore imageStore;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly IOptions<DataImporterOptions> dataImportOptions;

        public DataImporter(IFileSystem fileSystem, IDbContextFactory<SqlServerDataContext> dbFactory, IStaticAssetStore imageStore, IHttpClientFactory httpClientFactory, IOptions<DataImporterOptions> dataImportOptions)
        {
            if (dbFactory is null)
            {
                throw new ArgumentNullException(nameof(dbFactory));
            }

            this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            this.dbContext = dbFactory.CreateDbContext();
            this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
            this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            this.dataImportOptions = dataImportOptions ?? throw new ArgumentNullException(nameof(dataImportOptions));
        }

        private void Map(Release release, ReleaseFile releaseFile)
        {
            release.Slug = releaseFile.Slug;
            release.Isbn = releaseFile.Isbn;
            release.Locale = releaseFile.Locale;
            release.Upc = releaseFile.Upc;
            release.Asin = releaseFile.Asin;
            release.RegionCode = releaseFile.RegionCode;
            release.Year = releaseFile.Year;
            release.Title = releaseFile.Title;
            release.ImageUrl = releaseFile.ImageUrl;
            release.ReleaseDate = releaseFile.ReleaseDate;
        }

        private Series MapSeries(MetadataFile metadata)
        {
            var series = new Series();
            Map(series, metadata);
            return series;
        }

        private Movie MapMovie(MetadataFile metadata)
        {
            var movie = new Movie();
            Map(movie, metadata);
            return movie;
        }

        private void Map(MediaItem instance, MetadataFile metadata)
        {
            if (!string.IsNullOrEmpty(metadata.Slug) && HasInvalidChars(metadata.Slug))
            {
                AnsiConsole.WriteLine("Warning: Invalid slug char in " + metadata.Slug);
            }

            instance.Title = metadata.Title;
            instance.SortTitle = metadata.SortTitle;
            instance.FullTitle = metadata.FullTitle;
            instance.Externalids = metadata.ExternalIds;
            instance.Slug = metadata.Slug;
            instance.Year = metadata.Year;
            instance.ImageUrl = metadata.ImageUrl;
            instance.ReleaseDate = metadata.ReleaseDate;
            instance.LatestReleaseDate = metadata.ReleaseDate; // default to the release date but hopefully override with a releases release date

            instance.ContentRating = metadata.ContentRating;
            instance.Directors = metadata.Directors;
            instance.Stars = metadata.Stars;
            instance.Genres = metadata.Genres;
            instance.Plot = metadata.Plot;
            instance.Tagline = metadata.Tagline;
            instance.Runtime = metadata.Runtime;
            instance.RuntimeMinutes = metadata.RuntimeMinutes;
            instance.Writers = metadata.Writers;
        }

        public static bool HasInvalidChars(string slug)
        {
            if (string.IsNullOrEmpty(slug))
            {
                return false;
            }

            foreach (char c in slug)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                {
                    return true;
                }
            }

            return false;
        }

        private async Task ImportBoxset(string baseDirectory, string boxSetDirectory, BoxSetReleaseFile file, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(file.Slug) && HasInvalidChars(file.Slug))
            {
                AnsiConsole.WriteLine("Warning: Invalid slug char in " + file.Slug);
            }

            var boxset = await this.dbContext.BoxSets
                .Include(i => i.Release)
                .ThenInclude(r => r.Discs)
                .FirstOrDefaultAsync(s => s.Slug == file.Slug);
            if (boxset == null)
            {
                boxset = new Boxset
                {
                    Title = file.Title,
                    SortTitle = file.SortTitle,
                    Slug = file.Slug,
                    ImageUrl = file.ImageUrl,
                    Release = new Release
                    {
                        Slug = file.Slug,
                        Isbn = file.Isbn,
                        Locale = file.Locale,
                        Title = file.Title,
                        Upc = file.Upc,
                        Asin = file.Asin,
                        Year = file.Year,
                        RegionCode = file.RegionCode,
                        ReleaseDate = file.ReleaseDate,
                        DateAdded = DateTimeOffset.UtcNow
                    }
                };

                this.dbContext.BoxSets.Add(boxset);
            }
            else
            {
                // TODO: Is this correct logic? Seems backwards
                if (!dataImportOptions.Value.CleanImport)
                {
                    // clear the existing discs
                    boxset.Release.Discs.Clear();
                }
            }

            string imagePath = this.fileSystem.Path.Combine(boxSetDirectory, "front.jpg");
            if (await this.fileSystem.File.Exists(imagePath, cancellationToken))
            {
                string remotePath = string.Format("boxset/{0}.jpg", boxset.Slug);
                string url = await this.imageStore.Save(imagePath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                boxset.Release.ImageUrl = remotePath;
                file.ImageUrl = remotePath;
                boxset.ImageUrl = remotePath;

                // re-save the boxset file
                string json = JsonSerializer.Serialize(file, JsonOptions);
                await this.fileSystem.File.WriteAllText(this.fileSystem.Path.Combine(boxSetDirectory, BoxSetReleaseFile.Filename), json, cancellationToken);
            }

            foreach (var discInfo in file.Discs)
            {
                var foundDisc = await this.FindBoxsetDisc(baseDirectory, file, discInfo, cancellationToken);
                if (foundDisc != null)
                {
                    foundDisc.Index = discInfo.Index;
                    foundDisc.Name = discInfo.Name;
                    boxset.Release.Discs.Add(foundDisc);
                }
                else
                {
                    AnsiConsole.WriteLine("No disc found for disc {0} ({1}), slug: {2}", discInfo.Index, discInfo.Name, discInfo.Slug);
                }
            }

            await this.dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task<Disc> FindBoxsetDisc(string baseDirectory, BoxSetReleaseFile file, BoxSetDisc disc, CancellationToken cancellationToken = default)
        {
            // TODO: handle "mixed" sets
            string mediaTypeDirectory = this.fileSystem.Path.Combine(baseDirectory, file.Type.ToLower());
            if (await this.fileSystem.Directory.Exists(mediaTypeDirectory, cancellationToken))
            {
                await foreach (var mediaItemDirectory in this.fileSystem.Directory.EnumerateDirectories(mediaTypeDirectory, cancellationToken))
                {
                    string metaDataFile = this.fileSystem.Path.Combine(mediaItemDirectory, MetadataFile.Filename);
                    if (await this.fileSystem.File.Exists(metaDataFile, cancellationToken))
                    {
                        string json = await this.fileSystem.File.ReadAllText(metaDataFile, cancellationToken);
                        var metadata = JsonSerializer.Deserialize<MetadataFile>(json, JsonOptions);
                        if (!string.IsNullOrEmpty(metadata.Slug) && metadata.Slug.Equals(disc.TitleSlug, StringComparison.OrdinalIgnoreCase))
                        {
                            string releaseDirectory = this.fileSystem.Path.Combine(mediaItemDirectory, file.Slug);
                            if (await this.fileSystem.Directory.Exists(releaseDirectory, cancellationToken))
                            {
                                await foreach (var discJsonPath in this.fileSystem.Directory.EnumerateFiles(releaseDirectory, "disc*.json", cancellationToken))
                                {
                                    json = await this.fileSystem.File.ReadAllText(discJsonPath, cancellationToken);
                                    var discFile = JsonSerializer.Deserialize<Disc>(json, JsonOptions);
                                    if (!string.IsNullOrEmpty(discFile.Slug) && discFile.Slug.Equals(disc.Slug, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return discFile;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        private string FindBaseDataDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            var index = path.LastIndexOf(string.Format("{0}data{0}", this.fileSystem.Path.DirectorySeparatorChar));
            if (index > 0)
            {
                return path.Substring(0, index + 6);
            }

            return null;
        }

        public async Task Import(string inputDirectory, CancellationToken cancellationToken = default)
        {
            string baseDirectory = this.FindBaseDataDirectory(inputDirectory);
            if (baseDirectory == null)
            {
                AnsiConsole.WriteLine("Unable to determine base directory from '{0}'. Cannot import boxset", inputDirectory);
                return;
            }

            string boxsetFile = this.fileSystem.Path.Combine(inputDirectory, BoxSetReleaseFile.Filename);
            if (await this.fileSystem.File.Exists(boxsetFile, cancellationToken))
            {
                string json = await this.fileSystem.File.ReadAllText(boxsetFile, cancellationToken);
                var boxset = JsonSerializer.Deserialize<BoxSetReleaseFile>(json, JsonOptions);
                await ImportBoxset(baseDirectory, inputDirectory, boxset, cancellationToken);
                return;
            }

            string metaDataFile = this.fileSystem.Path.Combine(inputDirectory, MetadataFile.Filename);
            if (await this.fileSystem.File.Exists(metaDataFile, cancellationToken))
            {
                string json = await this.fileSystem.File.ReadAllText(metaDataFile, cancellationToken);
                var metadata = JsonSerializer.Deserialize<MetadataFile>(json, JsonOptions);
                var imdbFilePath = this.fileSystem.Path.Combine(inputDirectory, "imdb.json");
                TitleData imdbTitle = null;

                if (await this.fileSystem.File.Exists(imdbFilePath, cancellationToken))
                {
                    json = await this.fileSystem.File.ReadAllText(imdbFilePath, cancellationToken);
                    imdbTitle = JsonSerializer.Deserialize<TitleData>(json, JsonOptions);
                }

                if (string.IsNullOrEmpty(metadata.Type))
                {
                    AnsiConsole.WriteLine("Unable to determine type of item being imported (series or movie)");
                    return;
                }

                string type = metadata.Type;

                string coverImagePath = this.fileSystem.Path.Combine(inputDirectory, "cover.jpg");
                if (await this.fileSystem.File.Exists(coverImagePath, cancellationToken) /*&& string.IsNullOrEmpty(metadata.ImageUrl)*/)
                {
                    string remotePath = string.Format("{0}/{1}/{2}", type, metadata.Slug, "cover.jpg");
                    string url = await this.imageStore.Save(coverImagePath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                    metadata.ImageUrl = remotePath;

                    // re-save the metadata file
                    // TODO: This might cause files to change in local dev scenarios
                    json = JsonSerializer.Serialize(metadata, JsonOptions);
                    await this.fileSystem.File.WriteAllText(metaDataFile, json, cancellationToken);
                }

                MediaItem item = null;
                var dateAdded = DateTimeOffset.UtcNow;
                if (type.Equals("series", StringComparison.OrdinalIgnoreCase))
                {
                    var series = await this.dbContext.MediaItems.FirstOrDefaultAsync(s => s.Slug == metadata.Slug);
                    if (series != null)
                    {
                        this.Map(series, metadata);
                    }
                    else
                    {
                        series = MapSeries(metadata);
                        series.DateAdded = dateAdded;
                        this.dbContext.MediaItems.Add(series);
                    }

                    item = series;
                }
                else if (type.Equals("movie", StringComparison.OrdinalIgnoreCase))
                {
                    var movie = await this.dbContext.MediaItems
                        .Include(i => i.MediaItemGroups)
                        .ThenInclude(i => i.Group)
                        .Include(i => i.Releases)
                        .ThenInclude(r => r.Discs)
                        .FirstOrDefaultAsync(s => s.Slug == metadata.Slug);
                    if (movie != null)
                    {
                        this.Map(movie, metadata);
                    }
                    else
                    {
                        movie = MapMovie(metadata);
                        movie.DateAdded = dateAdded;
                        this.dbContext.MediaItems.Add(movie);
                    }

                    if (metadata.DateAdded.Year < 1990)
                    {
                        metadata.DateAdded = dateAdded;
                        json = JsonSerializer.Serialize(metadata, JsonOptions);
                        await this.fileSystem.File.WriteAllText(metaDataFile, json, cancellationToken);
                    }

                    item = movie;
                }

                //if (imdbTitle != null)
                //{
                //    await TryAddGroups(item, imdbTitle, cancellationToken);
                //}

                await foreach (var releaseFolder in this.fileSystem.Directory.EnumerateDirectories(inputDirectory, cancellationToken))
                {
                    string releaseFilePath = this.fileSystem.Path.Combine(releaseFolder, ReleaseFile.Filename);
                    if (await this.fileSystem.File.Exists(releaseFilePath, cancellationToken))
                    {
                        json = await this.fileSystem.File.ReadAllText(releaseFilePath, cancellationToken);
                        var releaseFile = JsonSerializer.Deserialize<ReleaseFile>(json, JsonOptions);

                        var release = item.Releases.FirstOrDefault(r => r.Slug.Equals(releaseFile.Slug, StringComparison.OrdinalIgnoreCase));
                        if (release == null)
                        {
                            release = new Release
                            {
                                DateAdded = releaseFile.DateAdded,
                            };
                            item.Releases.Add(release);

                            // Keep track of the most recent release date of all the releases for this item
                            if (releaseFile.ReleaseDate > item.LatestReleaseDate)
                            {
                                item.LatestReleaseDate = releaseFile.ReleaseDate;
                            }
                        }

                        Map(release, releaseFile);

                        string imagePath = this.fileSystem.Path.Combine(releaseFolder, "front.jpg");
                        if (await this.fileSystem.File.Exists(imagePath, cancellationToken))
                        {
                            bool releaseChanged = false;
                            string remotePath = string.Format("{0}/{1}/{2}.jpg", metadata.Type, metadata.Slug, release.Slug);
                            bool existsInBlobStorage = await this.imageStore.Exists(remotePath, cancellationToken);
                            if (string.IsNullOrEmpty(release.ImageUrl))
                            {
                                if (!existsInBlobStorage)
                                {
                                    string url = await this.imageStore.Save(imagePath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                                }

                                release.ImageUrl = remotePath;
                                releaseFile.ImageUrl = remotePath;
                                releaseChanged = true;
                            }
                            else
                            {
                                if (!existsInBlobStorage)
                                {
                                    string url = await this.imageStore.Save(imagePath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                                    if (releaseFile.ImageUrl != remotePath)
                                    {
                                        release.ImageUrl = remotePath;
                                        releaseFile.ImageUrl = remotePath;
                                        releaseChanged = true;
                                    }
                                }
                            }

                            if (releaseChanged)
                            {
                                // re-save the release file
                                json = JsonSerializer.Serialize(releaseFile, JsonOptions);
                                await this.fileSystem.File.WriteAllText(releaseFilePath, json, cancellationToken);
                            }
                        }

                        if (!dataImportOptions.Value.CleanImport)
                        {
                            release.Discs.Clear();
                        }

                        foreach (var file in await this.fileSystem.Directory.GetFiles(releaseFolder, "disc*.json", cancellationToken))
                        {
                            string fileName = this.fileSystem.Path.GetFileName(file);
                            if (fileName.StartsWith("disc", StringComparison.OrdinalIgnoreCase))
                            {
                                json = await this.fileSystem.File.ReadAllText(file, cancellationToken);
                                Disc disc = JsonSerializer.Deserialize<Disc>(json, JsonOptions);

                                release.Discs.Add(disc);
                            }
                        }
                    }
                }

                try
                {
                    await this.dbContext.SaveChangesAsync(cancellationToken);
                    return;
                }
                catch (Exception e)
                {
                    AnsiConsole.WriteLine($"Error Saving {inputDirectory}");
                    AnsiConsole.WriteException(e);
                    AnsiConsole.WriteLine();
                }
            }

            // Disable single release support for now
            return;

            string singleReleaseFile = this.fileSystem.Path.Combine(inputDirectory, ReleaseFile.Filename);
            if (await this.fileSystem.File.Exists(singleReleaseFile, cancellationToken))
            {
                // ensure we are adding a release to an existing item
                string json = await this.fileSystem.File.ReadAllText(singleReleaseFile, cancellationToken);
                var singleRelease = JsonSerializer.Deserialize<ReleaseFile>(json, JsonOptions);

                var parentFolder = await this.fileSystem.Directory.GetParent(inputDirectory);
                metaDataFile = this.fileSystem.Path.Combine(parentFolder.FullName, MetadataFile.Filename);

                if (await this.fileSystem.File.Exists(metaDataFile, cancellationToken))
                {
                    json = await this.fileSystem.File.ReadAllText(metaDataFile, cancellationToken);
                    var singleMetadata = JsonSerializer.Deserialize<MetadataFile>(json, JsonOptions);

                    if (string.IsNullOrEmpty(singleMetadata.Type))
                    {
                        AnsiConsole.WriteLine("Unable to determine type of item being imported (series or movie)");
                        return;
                    }

                    string type = singleMetadata.Type;

                    MediaItem item = null;
                    if (type.Equals("series", StringComparison.OrdinalIgnoreCase))
                    {
                        var series = await this.dbContext.MediaItems.FirstOrDefaultAsync(s => s.Slug == singleMetadata.Slug);
                        if (series != null)
                        {
                            this.Map(series, singleMetadata);
                        }
                        else
                        {
                            series = MapSeries(singleMetadata);
                            series.DateAdded = singleRelease.DateAdded;
                            this.dbContext.MediaItems.Add(series);
                        }

                        item = series;
                    }
                    else if (type.Equals("movie", StringComparison.OrdinalIgnoreCase))
                    {
                        var movie = await this.dbContext.MediaItems
                            .Include(i => i.Releases)
                            .ThenInclude(r => r.Discs)
                            .FirstOrDefaultAsync(s => s.Slug == singleMetadata.Slug);
                        if (movie != null)
                        {
                            this.Map(movie, singleMetadata);
                        }
                        else
                        {
                            movie = MapMovie(singleMetadata);
                            movie.DateAdded = singleRelease.DateAdded;
                            this.dbContext.MediaItems.Add(movie);
                        }

                        item = movie;
                    }

                    var release = item.Releases.FirstOrDefault(r => r.Slug.Equals(singleRelease.Slug, StringComparison.OrdinalIgnoreCase));
                    if (release == null)
                    {
                        release = new Release
                        {
                            DateAdded = singleRelease.DateAdded,
                        };
                        item.Releases.Add(release);

                        // Keep track of the most recent release date of all the releases for this item
                        if (singleRelease.ReleaseDate > item.LatestReleaseDate)
                        {
                            item.LatestReleaseDate = singleRelease.ReleaseDate;
                        }
                    }

                    Map(release, singleRelease);

                    string imagePath = this.fileSystem.Path.Combine(inputDirectory, "front.jpg");
                    if (await this.fileSystem.File.Exists(imagePath, cancellationToken) && string.IsNullOrEmpty(release.ImageUrl))
                    {
                        string remotePath = string.Format("{0}/{1}/{2}.jpg", singleMetadata.Type, singleMetadata.Slug, release.Slug);
                        string url = await this.imageStore.Save(imagePath, remotePath, ContentTypes.ImageContentType, cancellationToken);
                        release.ImageUrl = remotePath;
                        singleRelease.ImageUrl = remotePath;

                        // re-save the release file
                        json = JsonSerializer.Serialize(singleRelease, JsonOptions);
                        await this.fileSystem.File.WriteAllText(singleReleaseFile, json, cancellationToken);
                    }

                    if (!dataImportOptions.Value.CleanImport)
                    {
                        release.Discs.Clear();
                    }
                    foreach (var file in await this.fileSystem.Directory.GetFiles(inputDirectory, "disc*.json", cancellationToken))
                    {
                        string fileName = this.fileSystem.Path.GetFileName(file);
                        if (fileName.StartsWith("disc", StringComparison.OrdinalIgnoreCase))
                        {
                            json = await this.fileSystem.File.ReadAllText(file, cancellationToken);
                            Disc disc = JsonSerializer.Deserialize<Disc>(json, JsonOptions);

                            release.Discs.Add(disc);
                        }
                    }

                    try
                    {
                        await this.dbContext.SaveChangesAsync(cancellationToken);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error saving {0}: {1}", inputDirectory, e.Message);
                    }
                }
            }
        }

        public async Task ImportGroups(string inputDirectory, CancellationToken cancellationToken = default)
        {
            string boxsetFile = this.fileSystem.Path.Combine(inputDirectory, BoxSetReleaseFile.Filename);
            if (await this.fileSystem.File.Exists(boxsetFile, cancellationToken))
            {
                // no groups for boxsets for now
                return;
            }

            string metaDataFile = this.fileSystem.Path.Combine(inputDirectory, MetadataFile.Filename);
            if (await this.fileSystem.File.Exists(metaDataFile, cancellationToken))
            {
                string json = await this.fileSystem.File.ReadAllText(metaDataFile, cancellationToken);
                var metadata = JsonSerializer.Deserialize<MetadataFile>(json, JsonOptions);
                var imdbFilePath = this.fileSystem.Path.Combine(inputDirectory, "imdb.json");
                TitleData imdbTitle = null;

                if (await this.fileSystem.File.Exists(imdbFilePath, cancellationToken))
                {
                    json = await this.fileSystem.File.ReadAllText(imdbFilePath, cancellationToken);
                    imdbTitle = JsonSerializer.Deserialize<TitleData>(json, JsonOptions);
                }

                MediaItem item = null;
                string type = metadata.Type;
                if (type.Equals("series", StringComparison.OrdinalIgnoreCase))
                {
                    var series = await this.dbContext.MediaItems.FirstOrDefaultAsync(s => s.Slug == metadata.Slug);
                    if (series != null)
                    {
                        this.Map(series, metadata);
                    }
                    else
                    {
                        AnsiConsole.WriteLine("Series '{0}' not found", metadata.Slug);
                        return;
                    }

                    item = series;
                }
                else if (type.Equals("movie", StringComparison.OrdinalIgnoreCase))
                {
                    var movie = await this.dbContext.MediaItems
                        .Include(i => i.MediaItemGroups)
                        .ThenInclude(i => i.Group)
                        .FirstOrDefaultAsync(s => s.Slug == metadata.Slug);
                    if (movie != null)
                    {
                        this.Map(movie, metadata);
                    }
                    else
                    {
                        AnsiConsole.WriteLine("Movie '{0}' not found", metadata.Slug);
                        return;
                    }

                    item = movie;
                }

                bool shouldSave = false;
                if (imdbTitle != null)
                {
                    await TryAddGroups(item, imdbTitle, metadata, cancellationToken);
                    shouldSave = true;
                }
                else if (metadata.Groups.Count > 0)
                {
                    await TryAddCustomGroups(item, metadata, cancellationToken);
                    shouldSave = true;
                }

                if (shouldSave)
                {
                    try
                    {
                        await this.dbContext.SaveChangesAsync(cancellationToken);
                        return;
                    }
                    catch (Exception e)
                    {
                        AnsiConsole.WriteLine($"Error Saving {inputDirectory}");
                        AnsiConsole.WriteException(e.InnerException);
                    }
                }
            }
        }

        private async Task TryAddGroups(MediaItem item, TitleData imdb, MetadataFile metadata, CancellationToken cancellationToken)
        {
            await TryAddActors(item, imdb, cancellationToken);
            await TryAddGenres(item, imdb, cancellationToken);
            await TryAddRole(item, imdb.WriterList, Roles.Writer, cancellationToken);
            await TryAddRole(item, imdb.DirectorList, Roles.Director, cancellationToken);
            await TryAddCompanies(item, imdb, cancellationToken);
            await TryAddCustomGroups(item, metadata, cancellationToken);
        }

        private async Task TryAddCustomGroups(MediaItem item, MetadataFile metadata, CancellationToken cancellationToken)
        {
            foreach (var groupName in metadata.Groups)
            {
                var group = await TryGetGroup(groupName, null, cancellationToken);
                if (group == null)
                {
                    string slug = groupName.Slugify();

                    group = new Group
                    {
                        Name = groupName,
                        Slug = slug
                    };
                    groupCache.TryAdd(groupName, group);

                    var mig = new MediaItemGroup
                    {
                        Group = group,
                        MediaItem = item,
                        Role = Roles.Genre
                    };

                    item.MediaItemGroups.Add(mig);
                    mediaItemGroupCache.TryAdd($"{Roles.Genre}-{slug}-{item.Id}", mig);
                }
                else
                {
                    var mediaItemGroup = await TryGetMediaItemGroup(Roles.CustomGroup, group, item.Id, cancellationToken);
                    if (mediaItemGroup == null)
                    {
                        var newGroup = new MediaItemGroup
                        {
                            Group = group,
                            MediaItem = item,
                            Role = Roles.Genre
                        };
                        item.MediaItemGroups.Add(newGroup);
                        mediaItemGroupCache.TryAdd($"{Roles.CustomGroup}-{group.Slug}-{item.Id}", newGroup);
                    }
                }
            }
        }

        private async Task TryAddCompanies(MediaItem item, TitleData imdb, CancellationToken cancellationToken)
        {
            foreach (var company in imdb.CompanyList)
            {
                string companyName = company.Name;
                var mapping = this.dataImportOptions.Value.CompanyNameMappings.FirstOrDefault(m => m.FullName == companyName);
                if (mapping != null)
                {
                    companyName = mapping.ShortName;
                }

                var group = await TryGetGroup(companyName, null, cancellationToken);
                if (group == null)
                {
                    string slug = companyName.Slugify();

                    group = new Group
                    {
                        Name = companyName,
                        Slug = slug,
                        ImdbId = company.Id
                    };

                    groupCache.TryAdd(companyName, group);

                    var mig = new MediaItemGroup
                    {
                        Group = group,
                        MediaItem = item,
                        Role = Roles.Company
                    };
                    item.MediaItemGroups.Add(mig);

                    mediaItemGroupCache.TryAdd($"{Roles.Company}-{slug}-{item.Id}", mig);
                }
                else
                {
                    var mediaItemGroup = item.MediaItemGroups.FirstOrDefault(g => g.GroupId == group.Id && g.Role == Roles.Company);
                    if (mediaItemGroup == null || group.Id <= 0)
                    {
                        // group is not yet saved to the database
                        mediaItemGroup = item.MediaItemGroups.FirstOrDefault(g => g.Group.Name == companyName);
                    }

                    if (mediaItemGroup == null)
                    {
                        var newGroup = new MediaItemGroup
                        {
                            Group = group,
                            MediaItem = item,
                            Role = Roles.Company
                        };
                        item.MediaItemGroups.Add(newGroup);
                        mediaItemGroupCache.TryAdd($"{Roles.Company}-{companyName}-{item.Id}", newGroup);
                    }
                }
            }
        }

        private async Task TryAddRole(MediaItem item, IEnumerable<StarShort> items, string role, CancellationToken cancellationToken)
        {
            foreach (var person in items)
            {
                Group group = null;
                try
                {
                    group = await TryGetGroup(person.Name, person.Id, cancellationToken);
                }
                catch (NameCollisionException)
                {
                    string remotePath = $"groups/{person.Name.Slugify()}-{person.Id}.jpg";
                    group = new Group
                    {
                        Name = person.Name,
                        ImdbId = person.Id,
                        Slug = $"{person.Name.Slugify()}-{person.Id}", // this slug avoids a collision
                        ImageUrl = remotePath
                    };
                    groupCache.TryAdd(person.Id, group);

                    var mig = new MediaItemGroup
                    {
                        Group = group,
                        MediaItem = item,
                        Role = role,
                        IsFeatured = role == Roles.Director
                    };

                    item.MediaItemGroups.Add(mig);
                    mediaItemGroupCache.TryAdd($"{role}-{person.Id}-{item.Id}", mig);
                    continue;
                }

                if (group == null)
                {
                    string remotePath = $"groups/{person.Name.Slugify()}-{person.Id}.jpg";
                    bool exists = await this.imageStore.Exists(remotePath, cancellationToken);
                    //if (!exists)
                    //{
                    //    var fullName = await this.TryGetNameData(person.Id, cancellationToken);

                    //    if (fullName != null && !string.IsNullOrEmpty(fullName.Image))
                    //    {
                    //        bool success = await TryDownloadGroupImage(fullName.Image, remotePath, cancellationToken);
                    //        if (!success)
                    //        {
                    //            remotePath = null; // Don't store an url if we didn't upload one
                    //        }
                    //    }
                    //    else
                    //    {
                    //        remotePath = null; // Don't store an url if we didn't upload one
                    //    }
                    //}

                    group = new Group
                    {
                        Name = person.Name,
                        ImdbId = person.Id,
                        Slug = person.Name.Slugify(),
                        ImageUrl = remotePath,
                    };
                    groupCache.TryAdd(person.Id, group);

                    var mig = new MediaItemGroup
                    {
                        Group = group,
                        MediaItem = item,
                        Role = role,
                        IsFeatured = role == Roles.Director
                    };
                    item.MediaItemGroups.Add(mig);
                    mediaItemGroupCache.TryAdd($"{role}-{person.Id}-{item.Id}", mig);
                }
                else
                {
                    var mediaItemGroup = await TryGetMediaItemGroup(role, group, item.Id, cancellationToken);
                    if (mediaItemGroup == null)
                    {
                        var newGroup = new MediaItemGroup
                        {
                            Group = group,
                            MediaItem = item,
                            Role = role
                        };
                        item.MediaItemGroups.Add(newGroup);
                        mediaItemGroupCache.TryAdd($"{role}-{group.ImdbId}-{item.Id}", newGroup);
                    }
                }
            }
        }

        private HashSet<string> uploadedImages = new();

        private async Task TryAddGenres(MediaItem item, TitleData imdb, CancellationToken cancellationToken)
        {
            foreach (var genre in imdb.GenreList)
            {
                var group = await TryGetGroup(genre.Value, null, cancellationToken);
                if (group == null)
                {
                    string slug = genre.Value.Slugify();

                    group = new Group
                    {
                        Name = genre.Value,
                        Slug = slug
                    };
                    groupCache.TryAdd(genre.Value, group);

                    var mig = new MediaItemGroup
                    {
                        Group = group,
                        MediaItem = item,
                        Role = Roles.Genre
                    };

                    item.MediaItemGroups.Add(mig);
                    mediaItemGroupCache.TryAdd($"{Roles.Genre}-{slug}-{item.Id}", mig);
                }
                else
                {
                    var mediaItemGroup = await TryGetMediaItemGroup(Roles.Genre, group, item.Id, cancellationToken);
                    if (mediaItemGroup == null)
                    {
                        var newGroup = new MediaItemGroup
                        {
                            Group = group,
                            MediaItem = item,
                            Role = Roles.Genre
                        };
                        item.MediaItemGroups.Add(newGroup);
                        mediaItemGroupCache.TryAdd($"{Roles.Genre}-{group.Slug}-{item.Id}", newGroup);
                    }
                }
            }
        }

        private async Task TryAddActors(MediaItem item, TitleData imdb, CancellationToken cancellationToken)
        {
            foreach (var actor in imdb.ActorList)
            {
                bool isStar = imdb.Stars.Contains(actor.Name, StringComparison.OrdinalIgnoreCase);
                Group group = null;
                try
                {
                    group = await TryGetGroup(actor.Name, actor.Id, cancellationToken);
                }
                catch (NameCollisionException)
                {
                    string remotePath = $"groups/{actor.Name.Slugify()}-{actor.Id}.jpg";
                    group = new Group
                    {
                        Name = actor.Name,
                        ImdbId = actor.Id,
                        Slug = $"{actor.Name.Slugify()}-{actor.Id}", // this slug avoids a collision
                        ImageUrl = remotePath
                    };
                    groupCache.TryAdd(actor.Id, group);

                    var mig = new MediaItemGroup
                    {
                        Group = group,
                        MediaItem = item,
                        Role = Roles.Actor,
                        IsFeatured = isStar
                    };

                    item.MediaItemGroups.Add(mig);
                    mediaItemGroupCache.TryAdd($"{Roles.Actor}-{actor.Id}-{item.Id}", mig);
                    continue;
                }

                if (group == null)
                {
                    string remotePath = $"groups/{actor.Name.Slugify()}-{actor.Id}.jpg";

                    bool exists = await this.imageStore.Exists(remotePath, cancellationToken);
                    if (!exists)
                    {
                        bool success = await TryDownloadGroupImage(actor.Image, remotePath, cancellationToken);
                        if (!success)
                        {
                            remotePath = null;
                        }
                    }

                    group = new Group
                    {
                        Name = actor.Name,
                        ImdbId = actor.Id,
                        Slug = actor.Name.Slugify(),
                        ImageUrl = remotePath
                    };
                    groupCache.TryAdd(actor.Id, group);

                    var mig = new MediaItemGroup
                    {
                        Group = group,
                        MediaItem = item,
                        Role = Roles.Actor,
                        IsFeatured = isStar
                    };

                    item.MediaItemGroups.Add(mig);
                    mediaItemGroupCache.TryAdd($"{Roles.Actor}-{actor.Id}-{item.Id}", mig);
                }
                else
                {
                    var mediaItemGroup = await TryGetMediaItemGroup(Roles.Actor, group, item.Id, cancellationToken);
                    if (mediaItemGroup == null)
                    {
                        var newGroup = new MediaItemGroup
                        {
                            Group = group,
                            MediaItem = item,
                            Role = Roles.Actor,
                            IsFeatured = isStar
                        };
                        item.MediaItemGroups.Add(newGroup);
                        mediaItemGroupCache.TryAdd($"{Roles.Actor}-{group.ImdbId}-{item.Id}", newGroup);
                    }
                }
            }
        }

        private ConcurrentDictionary<string, NameData> nameDataCache = new();
        private async Task<NameData> TryGetNameData(string id, CancellationToken cancellationToken)
        {
            if (nameDataCache.TryGetValue(id, out NameData cachedItem))
            {
                return cachedItem;
            }

            // Note: used to have IMDB lookup here

            return null;
        }

        private ConcurrentDictionary<string, Group> groupCache = new();

        private async Task<Group> TryGetGroup(string name, string imdbId, CancellationToken cancellationToken)
        {
            string cacheKey = name;
            if (!string.IsNullOrEmpty(imdbId))
            {
                cacheKey = imdbId;
            }

            if (groupCache.TryGetValue(cacheKey, out Group cachedGroup))
            {
                return cachedGroup;
            }

            Group group = null;

            if (!string.IsNullOrEmpty(imdbId))
            {
                group = await this.dbContext.Groups.FirstOrDefaultAsync(g => g.ImdbId == imdbId, cancellationToken);

                if (group == null)
                {
                    // Check for collisions based on name
                    group = await this.dbContext.Groups.FirstOrDefaultAsync(g => g.Name == name, cancellationToken);

                    if (group != null)
                    {
                        // There is a name collision
                        throw new NameCollisionException(name, imdbId, group);
                    }
                }
            }
            else
            {
                group = await this.dbContext.Groups.FirstOrDefaultAsync(g => g.Name == name, cancellationToken);
            }

            if (group != null)
            {
                groupCache.TryAdd(cacheKey, group);
            }

            return group;
        }

        private ConcurrentDictionary<string, MediaItemGroup> mediaItemGroupCache = new();

        private async Task<MediaItemGroup> TryGetMediaItemGroup(string roleName, Group group, int mediaItemId, CancellationToken cancellationToken)
        {
            string cacheKey = $"{roleName}-{group.ImdbId}-{mediaItemId}";
            if (string.IsNullOrEmpty(group.ImdbId))
            {
                cacheKey = $"{roleName}-{group.Slug}-{mediaItemId}";
            }

            if (mediaItemGroupCache.TryGetValue(cacheKey, out MediaItemGroup cachedGroup))
            {
                return cachedGroup;
            }

            MediaItemGroup result = null;
            if (string.IsNullOrEmpty(group.ImdbId))
            {
                result = await this.dbContext.MediaItemGroup.FirstOrDefaultAsync(g => g.Role == roleName && g.Group.Name == group.Name && g.MediaItemId == mediaItemId, cancellationToken);
            }
            else
            {
                result = await this.dbContext.MediaItemGroup.FirstOrDefaultAsync(g => g.Role == roleName && g.Group.ImdbId == group.ImdbId && g.MediaItemId == mediaItemId, cancellationToken);
            }

            if (result != null)
            {
                mediaItemGroupCache.TryAdd(cacheKey, result);
            }

            return result;
        }

        private async Task<bool> TryDownloadGroupImage(string url, string remotePath, CancellationToken cancellationToken)
        {
            if (uploadedImages.Contains(remotePath))
            {
                return true;
            }

            try
            {
                var httpClient = this.httpClientFactory.CreateClient();
                using (var stream = await httpClient.GetStreamAsync(url))
                {
                    string remoteUrl = await this.imageStore.Save(stream, remotePath, ContentTypes.ImageContentType, cancellationToken);
                    uploadedImages.Add(remotePath);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
