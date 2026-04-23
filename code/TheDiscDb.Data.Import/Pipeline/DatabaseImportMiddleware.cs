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
                    if (item.MediaItem.DateAdded == default)
                    {
                        item.MediaItem.DateAdded = DateTime.UtcNow;
                    }
                    await this.HandleContributorsOnAdd(item.MediaItem, cancellationToken);
                    this.dbContext.MediaItems.Add(item.MediaItem);
                }

                await this.SaveChangesAsync();
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
                        if (item.Boxset.Release.DateAdded == default)
                        {
                            item.Boxset.Release.DateAdded = DateTime.UtcNow;
                        }
                    }
                    this.dbContext.BoxSets.Add(item.Boxset);
                }

                await this.SaveChangesAsync();
            }

            await this.Next(item, cancellationToken);
        }

        private async Task SaveChangesAsync()
        {
            try
            {
                await this.dbContext.SaveChangesAsync();
            }
            catch
            {
                // Detach pending entities so they don't pollute subsequent items.
                // After a failed SaveChanges, Added/Modified entities remain in the
                // change tracker. If the next item shares an entity (e.g. same
                // contributor), EF would try to insert duplicates.
                foreach (var entry in this.dbContext.ChangeTracker.Entries()
                    .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                    .ToList())
                {
                    entry.State = EntityState.Detached;
                }
                throw;
            }
        }

        private async Task HandleContributorsOnUpdate(MediaItem fromDatabase, MediaItem newItem, CancellationToken cancellationToken)
        {
            // Track contributors seen across releases to avoid duplicate inserts
            // (handles both within-release and cross-release duplicates)
            var seenContributors = new Dictionary<string, Contributor>(StringComparer.OrdinalIgnoreCase);

            // this is called after the releases have been cleaned up for updates, so we can assume the releases match
            foreach (var fromDatabaseRelease in fromDatabase.Releases)
            {
                var newRelease = newItem.Releases.FirstOrDefault(r => r.Slug == fromDatabaseRelease.Slug);
                if (newRelease != null)
                {
                    var resolvedContributors = new List<Contributor>();
                    foreach (var contributor in newRelease.Contributors)
                    {
                        if (contributor.Name == null)
                        {
                            continue;
                        }

                        // Already resolved this contributor name — reuse the same entity
                        if (seenContributors.TryGetValue(contributor.Name, out var seen))
                        {
                            resolvedContributors.Add(seen);
                            continue;
                        }

                        var databaseContributor = fromDatabaseRelease.Contributors
                            .FirstOrDefault(c => c.Name == contributor.Name);

                        if (databaseContributor == null || databaseContributor.Id == 0)
                        {
                            // Not in this release or pending add — check globally
                            var existingContributor = await this.dbContext.Contributors
                                .FirstOrDefaultAsync(c => c.Name == contributor.Name, cancellationToken);
                            if (existingContributor != null)
                            {
                                seenContributors[contributor.Name] = existingContributor;
                                resolvedContributors.Add(existingContributor);
                            }
                            else
                            {
                                var user = await this.userManager.FindByNameAsync(contributor.Name);
                                if (user != null)
                                {
                                    contributor.UserId = user.Id;
                                }
                                seenContributors[contributor.Name] = contributor;
                                resolvedContributors.Add(contributor);
                            }
                        }
                        else
                        {
                            // Tracked database contributor — reuse it
                            seenContributors[databaseContributor.Name] = databaseContributor;
                            resolvedContributors.Add(databaseContributor);
                        }
                    }

                    // Rebuild the collection on the database entity with resolved, deduplicated entities
                    fromDatabaseRelease.Contributors.Clear();
                    foreach (var contributor in resolvedContributors)
                    {
                        fromDatabaseRelease.Contributors.Add(contributor);
                    }
                }
            }
        }

        private async Task HandleContributorsOnAdd(MediaItem mediaItem, CancellationToken cancellationToken)
        {
            // Track contributors seen across releases to avoid duplicate inserts
            // (handles both within-release and cross-release duplicates)
            var seenContributors = new Dictionary<string, Contributor>(StringComparer.OrdinalIgnoreCase);

            foreach (var release in mediaItem.Releases)
            {
                var resolvedContributors = new List<Contributor>();
                foreach (var contributor in release.Contributors)
                {
                    if (contributor.Name == null)
                    {
                        continue;
                    }

                    // Already resolved this contributor name — reuse the same entity
                    if (seenContributors.TryGetValue(contributor.Name, out var seen))
                    {
                        resolvedContributors.Add(seen);
                        continue;
                    }

                    var existingContributor = await this.dbContext.Contributors
                        .FirstOrDefaultAsync(c => c.Name == contributor.Name, cancellationToken);
                    if (existingContributor != null)
                    {
                        seenContributors[contributor.Name] = existingContributor;
                        resolvedContributors.Add(existingContributor);
                    }
                    else
                    {
                        var user = await this.userManager.FindByNameAsync(contributor.Name);
                        if (user != null)
                        {
                            contributor.UserId = user.Id;
                        }
                        seenContributors[contributor.Name] = contributor;
                        resolvedContributors.Add(contributor);
                    }
                }

                // Rebuild the collection with resolved, deduplicated entities
                release.Contributors.Clear();
                foreach (var contributor in resolvedContributors)
                {
                    release.Contributors.Add(contributor);
                }
            }
        }
    }
}

