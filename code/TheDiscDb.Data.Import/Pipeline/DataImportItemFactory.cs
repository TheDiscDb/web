using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fantastic.FileSystem;
using IMDbApiLib.Models;
using Spectre.Console;
using TheDiscDb.ImportModels;
using TheDiscDb.InputModels;

namespace TheDiscDb.Data.Import.Pipeline;

public class DataImportItemFactory
{
    private readonly IFileSystem fileSystem;

    public DataImportItemFactory(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<IEnumerable<ImportItem>> FindMediaItemsToProcess(string baseDirectory, CancellationToken cancellationToken = default)
    {
        HashSet<string> mediaItemQueue = new();
        HashSet<string> boxsetQueue = new();

        await this.fileSystem.VisitAsync(baseDirectory, async (item, token) =>
        {
            var type = await DetermineType(item, cancellationToken);
            if (type == ImportFolderType.MediaItemRoot)
            {
                mediaItemQueue.Add(item.Path);
            }
            else if (type == ImportFolderType.Boxset)
            {
                boxsetQueue.Add(item.Path);
            }
            else if (type == ImportFolderType.Release)
            {
                mediaItemQueue.Add(this.fileSystem.Path.GetDirectoryName(item.Path));
            }
        });

        List<ImportItem> results = new();

        string mediaLibraryRoot = this.TryFindMediaItemRoot(baseDirectory);

        foreach (var inputDirectory in mediaItemQueue)
        {
            string metaDataFile = this.fileSystem.Path.Combine(inputDirectory, MetadataFile.Filename);
            if (await this.fileSystem.File.Exists(metaDataFile, cancellationToken))
            {
                string json = await this.fileSystem.File.ReadAllText(metaDataFile, cancellationToken);
                var metadata = JsonSerializer.Deserialize<MetadataFile>(json, DataImporter.JsonOptions);
                var imdbFilePath = this.fileSystem.Path.Combine(inputDirectory, "imdb.json");
                TitleData imdbTitle = null;

                if (metadata.DateAdded == default)
                {
                    AnsiConsole.WriteLine("Warning: DateAdded is not set in metadata file for {0}", metaDataFile);
                    metadata.DateAdded = metadata.ReleaseDate;

                    // re-save the metadata file
                    json = JsonSerializer.Serialize(metadata, DataImporter.JsonOptions);
                    await this.fileSystem.File.WriteAllText(metaDataFile, json, cancellationToken);
                }

                if (await this.fileSystem.File.Exists(imdbFilePath, cancellationToken))
                {
                    json = await this.fileSystem.File.ReadAllText(imdbFilePath, cancellationToken);
                    imdbTitle = JsonSerializer.Deserialize<TitleData>(json, DataImporter.JsonOptions);
                }

                if (string.IsNullOrEmpty(metadata.Type))
                {
                    // TODO: Throw exception here?
                    AnsiConsole.WriteLine("Unable to determine type of item being imported (series or movie)");
                    continue;
                }

                string type = metadata.Type;
                MediaItem item = null;
                if (type.Equals("series", StringComparison.OrdinalIgnoreCase))
                {
                    var series = MapSeries(metadata);
                    item = series;
                }
                else if (type.Equals("movie", StringComparison.OrdinalIgnoreCase))
                {
                    var movie = MapMovie(metadata);
                    item = movie;
                }

                await foreach (var releaseFolder in this.fileSystem.Directory.EnumerateDirectories(inputDirectory, cancellationToken))
                {
                    string releaseFilePath = this.fileSystem.Path.Combine(releaseFolder, ReleaseFile.Filename);
                    if (await this.fileSystem.File.Exists(releaseFilePath, cancellationToken))
                    {
                        json = await this.fileSystem.File.ReadAllText(releaseFilePath, cancellationToken);
                        var releaseFile = JsonSerializer.Deserialize<ReleaseFile>(json, DataImporter.JsonOptions);

                        if (releaseFile.DateAdded == default)
                        {
                            AnsiConsole.WriteLine("Warning: DateAdded is not set in release file for {0}", releaseFilePath);
                            releaseFile.DateAdded = releaseFile.ReleaseDate;

                            // re-save the release file
                            json = JsonSerializer.Serialize(releaseFile, DataImporter.JsonOptions);
                            await this.fileSystem.File.WriteAllText(releaseFilePath, json, cancellationToken);
                        }

                        var release = item.Releases.FirstOrDefault(r => r.Slug.Equals(releaseFile.Slug, StringComparison.OrdinalIgnoreCase));
                        if (release == null)
                        {
                            release = new Release
                            {
                                DateAdded = releaseFile.DateAdded,
                            };

                            item.Releases.Add(release);
                        }

                        Map(release, releaseFile);

                        // Keep track of the most recent release date of all the releases for this item
                        if (releaseFile.ReleaseDate > item.LatestReleaseDate)
                        {
                            item.LatestReleaseDate = releaseFile.ReleaseDate;
                        }

                        foreach (var file in await this.fileSystem.Directory.GetFiles(releaseFolder, "disc*.json", cancellationToken))
                        {
                            string fileName = this.fileSystem.Path.GetFileName(file);
                            if (fileName.StartsWith("disc", StringComparison.OrdinalIgnoreCase))
                            {
                                json = await this.fileSystem.File.ReadAllText(file, cancellationToken);
                                Disc disc = JsonSerializer.Deserialize<Disc>(json, DataImporter.JsonOptions);

                                release.Discs.Add(disc);
                            }
                        }
                    }
                }

                results.Add(new ImportItem
                {
                    MediaItem = item,
                    ImdbData = imdbTitle,
                    Metadata = metadata,
                    BasePath = inputDirectory
                });
            }
        }

        foreach (var inputDirectory in boxsetQueue)
        {
            Boxset boxset = null;
            string boxsetFile = this.fileSystem.Path.Combine(inputDirectory, BoxSetReleaseFile.Filename);
            if (await this.fileSystem.File.Exists(boxsetFile, cancellationToken))
            {
                string json = await this.fileSystem.File.ReadAllText(boxsetFile, cancellationToken);
                var file = JsonSerializer.Deserialize<BoxSetReleaseFile>(json, DataImporter.JsonOptions);

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

                foreach (var discInfo in file.Discs)
                {
                    var foundDisc = await this.FindBoxsetDisc(mediaLibraryRoot, file, discInfo, cancellationToken);
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

                results.Add(new ImportItem
                {
                    BasePath = inputDirectory,
                    Boxset = boxset
                });
            }
        }

        return results;
    }

    private string TryFindMediaItemRoot(string baseDirectory)
    {
        var span = baseDirectory.AsSpan();
        var index = span.IndexOf("data\\sets", StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return span.Slice(0, index + 4).ToString();
        }

        return baseDirectory;
    }

    private async Task<Disc> FindBoxsetDisc(string baseDirectory, BoxSetReleaseFile file, BoxSetDisc disc, CancellationToken cancellationToken = default)
    {
        if (baseDirectory.EndsWith("sets", StringComparison.OrdinalIgnoreCase) || baseDirectory.EndsWith("sets" + fileSystem.Path.PathSeparator, StringComparison.OrdinalIgnoreCase))
        {
            baseDirectory = this.fileSystem.Path.GetDirectoryName(baseDirectory);
        }

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
                    var metadata = JsonSerializer.Deserialize<MetadataFile>(json, DataImporter.JsonOptions);
                    if (!string.IsNullOrEmpty(metadata.Slug) && metadata.Slug.Equals(disc.TitleSlug, StringComparison.OrdinalIgnoreCase))
                    {
                        string releaseDirectory = this.fileSystem.Path.Combine(mediaItemDirectory, file.Slug);
                        if (await this.fileSystem.Directory.Exists(releaseDirectory, cancellationToken))
                        {
                            await foreach (var discJsonPath in this.fileSystem.Directory.EnumerateFiles(releaseDirectory, "disc*.json", cancellationToken))
                            {
                                json = await this.fileSystem.File.ReadAllText(discJsonPath, cancellationToken);
                                var discFile = JsonSerializer.Deserialize<Disc>(json, DataImporter.JsonOptions);
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
        release.DateAdded = releaseFile.DateAdded;

        foreach (var contributor in releaseFile.Contributors)
        {
            release.Contributors.Add(new InputModels.Contributor
            {
                Name = contributor.Name,
                Source = contributor.Source
            });
        }
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
        instance.DateAdded = metadata.DateAdded;

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

    private async Task<ImportFolderType> DetermineType(FileItem item, CancellationToken cancellationToken)
    {
        if (item.Type != FileItemType.Directory)
        {
            return ImportFolderType.Unknown;
        }

        await foreach (var file in this.fileSystem.Directory.EnumerateFiles(item.Path))
        {
            string fileName = this.fileSystem.Path.GetFileName(file);
            if (fileName.Equals(ReleaseFile.Filename, StringComparison.OrdinalIgnoreCase))
            {
                return ImportFolderType.Release;
            }
            else if (fileName.Equals(MetadataFile.Filename, StringComparison.OrdinalIgnoreCase))
            {
                return ImportFolderType.MediaItemRoot;
            }
            else if (fileName.Equals(BoxSetReleaseFile.Filename, StringComparison.OrdinalIgnoreCase))
            {
                return ImportFolderType.Boxset;
            }
        }

        return ImportFolderType.Unknown;
    }

    private enum ImportFolderType
    {
        Unknown,
        MediaItemRoot,
        Release,
        Boxset
    }

}
