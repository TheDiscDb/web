namespace TheDiscDb.Data.Import.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using TheDiscDb.InputModels;
    using TheDiscDb.Web.Data;

    public class DatabaseImportMiddleware : IMiddleware, IAsyncDisposable
    {
        private readonly SqlServerDataContext dbContext;
        private readonly IItemHandler<MediaItem> mediaItemHandler;
        private readonly IItemHandler<Boxset> boxsetItemHandler;
        private readonly UserManager<TheDiscDbUser> userManager;

        public DatabaseImportMiddleware(IDbContextFactory<SqlServerDataContext> dbFactory, IItemHandler<MediaItem> mediaItemHandler, IItemHandler<Boxset> boxsetItemHandler, UserManager<TheDiscDbUser> userManager)
        {
            this.dbContext = dbFactory.CreateDbContext();
            this.mediaItemHandler = mediaItemHandler ?? throw new ArgumentNullException(nameof(mediaItemHandler));
            this.boxsetItemHandler = boxsetItemHandler ?? throw new ArgumentNullException(nameof(boxsetItemHandler));
            this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            this.dbContext?.Dispose();
        }

        public async Task Process(ImportItem item, CancellationToken cancellationToken)
        {
            if (item.MediaItem != null)
            {
                var fromDatabase = await this.dbContext.MediaItems
                            .Include(i => i.MediaItemGroups)
                            .ThenInclude(i => i.Group)
                            .Include(i => i.Releases)
                            .ThenInclude(r => r.Discs)
                            .ThenInclude(d => d.Titles)
                            .ThenInclude(t => t.Item)
                            .Include(i => i.Releases) // repeat the path for release => disc => title to also include tracks
                            .ThenInclude(r => r.Discs)
                            .ThenInclude(d => d.Titles)
                            .ThenInclude(t => t.Tracks)
                            .Include(i => i.MediaItemGroups)
                            .ThenInclude(m => m.Group)
                            .Include(i => i.Releases)
                            .ThenInclude(r => r.Contributors)
                            .Include(i => i.Releases)
                            .ThenInclude(r => r.ReleaseGroups)
                            .ThenInclude(rg => rg.Group)
                            .FirstOrDefaultAsync(s => s.Type == item.MediaItem.Type && s.Slug == item.MediaItem.Slug);
                if (fromDatabase != null)
                {
                    this.mediaItemHandler.TryUpdate(fromDatabase, item.MediaItem);
                    await HandleContributorsOnUpdate(fromDatabase, item.MediaItem, cancellationToken);
                    item.MediaItem = fromDatabase; // allows changes made in other middleware to be tracked by the DB
                }
                else
                {
                    item.MediaItem.DateAdded = DateTime.UtcNow;
                    await this.HandleContributorsOnAdd(item.MediaItem, cancellationToken);
                    this.dbContext.MediaItems.Add(item.MediaItem);
                }

                await this.dbContext.SaveChangesAsync();
            }
            else if (item.Boxset != null)
            {
                var fromDatabase = await this.dbContext.BoxSets
                    .Include(i => i.Release)
                    .ThenInclude(r => r!.Discs)
                    .ThenInclude(d => d.Titles)
                    .ThenInclude(t => t.Item)
                    .FirstOrDefaultAsync(s => s.Slug == item.Boxset.Slug);

                if (fromDatabase != null)
                {
                    this.boxsetItemHandler.TryUpdate(fromDatabase, item.Boxset);
                    item.Boxset = fromDatabase;
                }
                else
                {
                    if (item.Boxset.Release != null)
                    {
                        item.Boxset.Release.DateAdded = DateTime.UtcNow;
                    }
                    this.dbContext.BoxSets.Add(item.Boxset);
                }

                await this.dbContext.SaveChangesAsync();
            }

            await this.Next(item, cancellationToken);
        }

        private async Task HandleContributorsOnUpdate(MediaItem fromDatabase, MediaItem newItem, CancellationToken cancellationToken)
        {
            // this is called after the releases have been cleaned up for updates, so we can assume the releases match
            foreach (var fromDatabaseRelease in fromDatabase.Releases)
            {
                var newRelease = newItem.Releases.FirstOrDefault(r => r.Slug == fromDatabaseRelease.Slug);
                if (newRelease != null)
                {
                    var addedContributors = new List<Contributor>();
                    foreach (var contributor in newRelease.Contributors)
                    {
                        var databaseContributor = fromDatabaseRelease.Contributors
                            .FirstOrDefault(c => c.Name == contributor.Name);

                        if (databaseContributor == null)
                        {
                            // Not in this release, check globally
                            var existingContributor = await this.dbContext.Contributors
                                .FirstOrDefaultAsync(c => c.Name == contributor.Name, cancellationToken);
                            if (existingContributor != null)
                            {
                                addedContributors.Add(existingContributor);
                            }
                            else
                            {
                                // New contributor, try looking up userId
                                if (contributor.Name != null)
                                {
                                    var user = await this.userManager.FindByNameAsync(contributor.Name);
                                    if (user != null)
                                    {
                                        contributor.UserId = user.Id;
                                    }
                                }
                                fromDatabaseRelease.Contributors.Add(contributor);
                            }
                        }
                        else
                        {
                            if (databaseContributor.Id == 0)
                            {
                                // this is a pending add so do the add logic here
                                // This happens when a new realease is being added to an existing media item
                                var existingContributor = await this.dbContext.Contributors
                                    .FirstOrDefaultAsync(c => c.Name == contributor.Name, cancellationToken);
                                if (existingContributor != null)
                                {
                                    addedContributors.Add(existingContributor);
                                }
                                else
                                {
                                    // New contributor, try looking up userId
                                    if (contributor.Name != null)
                                    {
                                        var user = await this.userManager.FindByNameAsync(contributor.Name);
                                        if (user != null)
                                        {
                                            contributor.UserId = user.Id;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // This contributor is in the database but needs to be added to this release
                                addedContributors.Add(databaseContributor);
                            }
                        }
                    }

                    // Replace contributors with the tracked entities
                    foreach (var contributor in addedContributors)
                    {
                        var toRemove = newRelease.Contributors.FirstOrDefault(c => c.Name == contributor.Name);
                        if (toRemove != null)
                        {
                            newRelease.Contributors.Remove(toRemove);
                        }
                        newRelease.Contributors.Add(contributor);
                    }
                }
            }
        }

        private async Task HandleContributorsOnAdd(MediaItem mediaItem, CancellationToken cancellationToken)
        {
            foreach (var release in mediaItem.Releases)
            {
                var addedContributors = new List<Contributor>();
                foreach (var contributor in release.Contributors)
                {
                    var existingContributor = await this.dbContext.Contributors
                        .FirstOrDefaultAsync(c => c.Name == contributor.Name, cancellationToken);
                    if (existingContributor != null)
                    {
                        addedContributors.Add(existingContributor);
                    }
                    else
                    {
                        // New contributor, try looking up userId
                        if (contributor.Name != null)
                        {
                            var user = await this.userManager.FindByNameAsync(contributor.Name);
                            if (user != null)
                            {
                                contributor.UserId = user.Id;
                            }
                        }
                    }
                }

                // Replace contributors with the tracked entities
                foreach (var contributor in addedContributors)
                {
                    var toRemove = release.Contributors.FirstOrDefault(c => c.Name == contributor.Name);
                    if (toRemove != null)
                    {
                        release.Contributors.Remove(toRemove);
                    }
                    release.Contributors.Add(contributor);
                }
            }
        }
    }
}

