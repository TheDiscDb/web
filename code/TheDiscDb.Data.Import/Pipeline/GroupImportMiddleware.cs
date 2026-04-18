namespace TheDiscDb.Data.Import.Pipeline
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
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

    public class GroupImportMiddleware : IMiddleware
    {
        private readonly IDbContextFactory<SqlServerDataContext> dbFactory;
        private readonly HttpClient httpClient;
        private readonly IOptions<DataImporterOptions> dataImportOptions;
        private readonly IStaticAssetStore imageStore;
        private readonly IFileSystem fileSystem;
        private readonly SqlServerDataContext dbContext;

        public GroupImportMiddleware(IDbContextFactory<SqlServerDataContext> dbFactory, HttpClient httpClient, IOptions<DataImporterOptions> dataImportOptions, IStaticAssetStore imageStore, IFileSystem fileSystem)
        {
            this.dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.dataImportOptions = dataImportOptions ?? throw new ArgumentNullException(nameof(dataImportOptions));
            this.imageStore = imageStore ?? throw new ArgumentNullException(nameof(imageStore));
            this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            this.dbContext = dbFactory.CreateDbContext();
        }

        public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

        public async Task Process(ImportItem item, CancellationToken cancellationToken)
        {
            if (item.MediaItem != null)
            {
                bool shouldSave = false;
                this.dbContext.Attach(item.MediaItem);

                // Collect custom Groups from release.json files and merge them
                // into metadata so they are processed alongside title-level groups.
                // Also returns a per-release mapping so we can create ReleaseGroup entries.
                var releaseGroupMap = await CollectReleaseGroups(item, cancellationToken);

                if (item.ImdbData != null)
                {
                    await TryAddGroups(item.MediaItem, item.ImdbData, item.Metadata, cancellationToken);
                    shouldSave = true;
                }
                else if (item.TmdbData != null)
                {
                    await TryAddGroupsFromTmdb(item.MediaItem, item.TmdbData, cancellationToken);
                    await TryAddCustomGroups(item.MediaItem, item.Metadata, cancellationToken);
                    shouldSave = true;
                }
                else if (item.Metadata.Groups.Count > 0)
                {
                    await TryAddCustomGroups(item.MediaItem, item.Metadata, cancellationToken);
                    shouldSave = true;
                }

                // Create ReleaseGroup join entries for groups that came from release.json files
                if (releaseGroupMap.Count > 0)
                {
                    await TryAddReleaseGroups(item.MediaItem, releaseGroupMap, cancellationToken);
                    shouldSave = true;
                }

                if (shouldSave)
                {
                    try
                    {
                        await this.dbContext.SaveChangesAsync(cancellationToken);
                    }
                    catch (Exception e)
                    {
                        AnsiConsole.WriteLine($"Error Saving {item.Metadata.FullTitle}");
                        AnsiConsole.WriteException(e.InnerException ?? e);
                    }
                }
            }

            await this.Next(item, cancellationToken);
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
                var group = await TryGetGroup(groupName, null!, cancellationToken);
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

                    this.dbContext.MediaItemGroup.Add(mig);
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

        private async Task TryAddGroupsFromTmdb(MediaItem item, TmdbMetadata tmdb, CancellationToken cancellationToken)
        {
            foreach (var genre in tmdb.GenreList)
            {
                await TryAddNameGroup(item, genre, Roles.Genre, isFeatured: false, cancellationToken);
            }

            foreach (var director in tmdb.DirectorList)
            {
                await TryAddNameGroup(item, director, Roles.Director, isFeatured: true, cancellationToken);
            }

            foreach (var writer in tmdb.WriterList)
            {
                await TryAddNameGroup(item, writer, Roles.Writer, isFeatured: false, cancellationToken);
            }

            foreach (var star in tmdb.StarList)
            {
                await TryAddNameGroup(item, star, Roles.Actor, isFeatured: false, cancellationToken);
            }
        }

        private async Task TryAddNameGroup(MediaItem item, string name, string role, bool isFeatured, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            var group = await TryGetGroup(name, null!, cancellationToken);
            if (group == null)
            {
                string slug = name.Slugify();

                group = new Group
                {
                    Name = name,
                    Slug = slug
                };
                groupCache.TryAdd(name, group);

                var mig = new MediaItemGroup
                {
                    Group = group,
                    MediaItem = item,
                    Role = role,
                    IsFeatured = isFeatured
                };

                this.dbContext.MediaItemGroup.Add(mig);
                item.MediaItemGroups.Add(mig);
                mediaItemGroupCache.TryAdd($"{role}-{slug}-{item.Id}", mig);
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
                        Role = role,
                        IsFeatured = isFeatured
                    };
                    item.MediaItemGroups.Add(newGroup);
                    mediaItemGroupCache.TryAdd($"{role}-{group.Slug}-{item.Id}", newGroup);
                }
            }
        }

        private async Task<Dictionary<string, List<string>>> CollectReleaseGroups(ImportItem item, CancellationToken cancellationToken)
        {
            var releaseGroupMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(item.BasePath))
            {
                return releaseGroupMap;
            }

            if (!await this.fileSystem.Directory.Exists(item.BasePath, cancellationToken))
            {
                return releaseGroupMap;
            }

            await foreach (var releaseDir in this.fileSystem.Directory.EnumerateDirectories(item.BasePath, cancellationToken))
            {
                string releasePath = this.fileSystem.Path.Combine(releaseDir, ReleaseFile.Filename);
                if (!await this.fileSystem.File.Exists(releasePath, cancellationToken))
                {
                    continue;
                }

                try
                {
                    string json = await this.fileSystem.File.ReadAllText(releasePath, cancellationToken);
                    var releaseFile = JsonSerializer.Deserialize<ReleaseFile>(json, JsonHelper.JsonOptions);

                    if (releaseFile?.Groups != null)
                    {
                        foreach (var groupName in releaseFile.Groups)
                        {
                            if (!string.IsNullOrWhiteSpace(groupName))
                            {
                                // Still merge into metadata for MediaItemGroup creation
                                if (!item.Metadata.Groups.Contains(groupName))
                                {
                                    item.Metadata.Groups.Add(groupName);
                                }

                                // Track per-release mapping for ReleaseGroup creation
                                if (!string.IsNullOrEmpty(releaseFile.Slug))
                                {
                                    if (!releaseGroupMap.TryGetValue(releaseFile.Slug, out var groups))
                                    {
                                        groups = new List<string>();
                                        releaseGroupMap[releaseFile.Slug] = groups;
                                    }
                                    if (!groups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                                    {
                                        groups.Add(groupName);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip release files that can't be read or parsed
                }
            }

            return releaseGroupMap;
        }

        private async Task TryAddReleaseGroups(MediaItem item, Dictionary<string, List<string>> releaseGroupMap, CancellationToken cancellationToken)
        {
            foreach (var (releaseSlug, groupNames) in releaseGroupMap)
            {
                var release = item.Releases.FirstOrDefault(r => string.Equals(r.Slug, releaseSlug, StringComparison.OrdinalIgnoreCase));
                if (release == null)
                {
                    continue;
                }

                foreach (var groupName in groupNames)
                {
                    var group = await TryGetGroup(groupName, null!, cancellationToken);
                    if (group == null)
                    {
                        // Group should already have been created by TryAddCustomGroups,
                        // but check the local cache for groups not yet saved to the database.
                        if (!groupCache.TryGetValue(groupName, out group))
                        {
                            continue;
                        }
                    }

                    // Check if this ReleaseGroup already exists
                    var existingReleaseGroup = release.ReleaseGroups
                        .FirstOrDefault(rg => rg.Group != null && string.Equals(rg.Group.Name, groupName, StringComparison.OrdinalIgnoreCase));

                    if (existingReleaseGroup == null && group.Id != 0)
                    {
                        existingReleaseGroup = release.ReleaseGroups
                            .FirstOrDefault(rg => rg.GroupId == group.Id);
                    }

                    if (existingReleaseGroup == null && release.Id != 0)
                    {
                        existingReleaseGroup = await this.dbContext.ReleaseGroups
                            .FirstOrDefaultAsync(rg => rg.ReleaseId == release.Id && rg.GroupId == group.Id, cancellationToken);
                    }

                    if (existingReleaseGroup == null)
                    {
                        release.ReleaseGroups.Add(new ReleaseGroup
                        {
                            Release = release,
                            Group = group
                        });
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

                var group = await TryGetGroup(companyName, null!, cancellationToken);
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
                        mediaItemGroup = item.MediaItemGroups.FirstOrDefault(g => g.Group != null && g.Group.Name == companyName);
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
                Group? group = null;
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
                var group = await TryGetGroup(genre.Value, null!, cancellationToken);
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
                Group? group = null;
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
                    string? remotePath = $"groups/{actor.Name.Slugify()}-{actor.Id}.jpg";

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

        private ConcurrentDictionary<string, Group> groupCache = new();

        private async Task<Group?> TryGetGroup(string name, string imdbId, CancellationToken cancellationToken)
        {
            string cacheKey = name;
            if (!string.IsNullOrEmpty(imdbId))
            {
                cacheKey = imdbId;
            }

            if (groupCache.TryGetValue(cacheKey, out Group? cachedGroup))
            {
                return cachedGroup;
            }

            Group? group = null;

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

        private async Task<MediaItemGroup?> TryGetMediaItemGroup(string roleName, Group group, int mediaItemId, CancellationToken cancellationToken)
        {
            string cacheKey = $"{roleName}-{group.ImdbId}-{mediaItemId}";
            if (string.IsNullOrEmpty(group.ImdbId))
            {
                cacheKey = $"{roleName}-{group.Slug}-{mediaItemId}";
            }

            if (mediaItemGroupCache.TryGetValue(cacheKey, out MediaItemGroup? cachedGroup))
            {
                return cachedGroup;
            }

            MediaItemGroup? result = null;
            if (string.IsNullOrEmpty(group.ImdbId))
            {
                result = await this.dbContext.MediaItemGroup.FirstOrDefaultAsync(g => g.Role == roleName && g.Group != null && g.Group.Name == group.Name && g.MediaItemId == mediaItemId, cancellationToken);
            }
            else
            {
                result = await this.dbContext.MediaItemGroup.FirstOrDefaultAsync(g => g.Role == roleName && g.Group != null && g.Group.ImdbId == group.ImdbId && g.MediaItemId == mediaItemId, cancellationToken);
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

            if (string.IsNullOrEmpty(url) || url.Contains("imdb-api.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var stream = await this.httpClient.GetStreamAsync(url))
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

