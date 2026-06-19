namespace TheDiscDb.Data.Import.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using TheDiscDb.InputModels;
    using TheDiscDb.Web.Data;

    public class DatabaseImportMiddleware : IMiddleware, IAsyncDisposable
    {
        private readonly IDbContextFactory<SqlServerDataContext> dbFactory;
        private SqlServerDataContext dbContext;
        private readonly IItemHandler<MediaItem> mediaItemHandler;
        private readonly IItemHandler<Boxset> boxsetItemHandler;
        // UserManager<T> is scoped; this singleton resolves it per-use via a short-lived scope.
        private readonly IServiceScopeFactory scopeFactory;

        public DatabaseImportMiddleware(IDbContextFactory<SqlServerDataContext> dbFactory, IItemHandler<MediaItem> mediaItemHandler, IItemHandler<Boxset> boxsetItemHandler, IServiceScopeFactory scopeFactory)
        {
            this.dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            this.mediaItemHandler = mediaItemHandler ?? throw new ArgumentNullException(nameof(mediaItemHandler));
            this.boxsetItemHandler = boxsetItemHandler ?? throw new ArgumentNullException(nameof(boxsetItemHandler));
            this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

        private SqlServerDataContext GetDbContext()
        {
            if (this.dbContext == null)
            {
                this.dbContext = this.dbFactory.CreateDbContext();
            }

            return this.dbContext;
        }

        public async ValueTask DisposeAsync()
        {
            this.dbContext?.Dispose();
        }

        public async Task Process(ImportItem item, CancellationToken cancellationToken)
        {
            // The middleware (and therefore this DbContext) is a singleton reused across
            // every pipeline Run/ProcessItem call. Without resetting the change tracker,
            // entities loaded for a previous item stay tracked and collide with entities
            // materialized for the next item (e.g. a shared MediaItemGroup throws an
            // identity conflict during DetectChanges).
            this.GetDbContext().ChangeTracker.Clear();

            if (item.MediaItem != null)
            {
                var canonicalByHashAndFormat = new Dictionary<string, Disc>(StringComparer.OrdinalIgnoreCase);
                foreach (var release in item.MediaItem.Releases)
                {
                    await this.CanonicalizeReleaseDiscs(release.Discs, canonicalByHashAndFormat, cancellationToken);
                }

                var fromDatabase = await this.GetDbContext().MediaItems
                            .Include(i => i.Externalids)
                            .Include(i => i.MediaItemGroups)
                            .ThenInclude(i => i.Group)
                            .Include(i => i.Releases)
                            .ThenInclude(r => r.Discs)
                            .ThenInclude(d => d.Disc)
                            .ThenInclude(d => d.Titles)
                            .ThenInclude(t => t.Item)
                            .Include(i => i.Releases) // repeat the path for release => disc => title to also include tracks
                            .ThenInclude(r => r.Discs)
                            .ThenInclude(d => d.Disc)
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
                    foreach (var release in fromDatabase.Releases)
                    {
                        await this.CanonicalizeReleaseDiscs(release.Discs, canonicalByHashAndFormat, cancellationToken);
                    }
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
                    this.GetDbContext().MediaItems.Add(item.MediaItem);
                }

                await this.SaveChangesAsync();
            }
            else if (item.Boxset != null)
            {
                var canonicalByHashAndFormat = new Dictionary<string, Disc>(StringComparer.OrdinalIgnoreCase);
                if (item.Boxset.Release != null)
                {
                    await this.CanonicalizeReleaseDiscs(item.Boxset.Release.Discs, canonicalByHashAndFormat, cancellationToken);
                }

                var fromDatabase = await this.GetDbContext().BoxSets
                    .Include(i => i.Release)
                    .ThenInclude(r => r!.Discs)
                    .ThenInclude(d => d.Disc)
                    .ThenInclude(d => d.Titles)
                    .ThenInclude(t => t.Item)
                    .FirstOrDefaultAsync(s => s.Slug == item.Boxset.Slug);

                if (fromDatabase != null)
                {
                    this.boxsetItemHandler.TryUpdate(fromDatabase, item.Boxset);
                    if (fromDatabase.Release != null)
                    {
                        await this.CanonicalizeReleaseDiscs(fromDatabase.Release.Discs, canonicalByHashAndFormat, cancellationToken);
                    }
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
                    this.GetDbContext().BoxSets.Add(item.Boxset);
                }

                await this.SaveChangesAsync();
            }

            await this.Next(item, cancellationToken);
        }

        private async Task CanonicalizeReleaseDiscs(ICollection<ReleaseDisc> releaseDiscs, Dictionary<string, Disc> canonicalByHashAndFormat, CancellationToken cancellationToken)
        {
            var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenIndexes = new HashSet<int>();

            foreach (var releaseDisc in releaseDiscs)
            {
                if (releaseDisc?.Disc == null)
                {
                    continue;
                }

                if (releaseDisc.Index <= 0 || !seenIndexes.Add(releaseDisc.Index))
                {
                    var nextIndex = 1;
                    while (seenIndexes.Contains(nextIndex))
                    {
                        nextIndex++;
                    }

                    releaseDisc.Index = nextIndex;
                    seenIndexes.Add(nextIndex);
                }

                if (!string.IsNullOrWhiteSpace(releaseDisc.Slug))
                {
                    var normalizedSlug = releaseDisc.Slug.Trim();
                    if (!seenSlugs.Add(normalizedSlug))
                    {
                        // IX_ReleaseDiscs_ReleaseId_Slug is unique (filtered). Keep the first
                        // occurrence and null out duplicates so import can proceed.
                        releaseDisc.Slug = null;
                    }
                    else
                    {
                        releaseDisc.Slug = normalizedSlug;
                    }
                }

                var inputDisc = releaseDisc.Disc;
                var normalizedFormat = inputDisc.Format?.Trim();
                var normalizedContentHash = inputDisc.ContentHash?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedFormat) || string.IsNullOrWhiteSpace(normalizedContentHash))
                {
                    continue;
                }

                inputDisc.Format = normalizedFormat;
                inputDisc.ContentHash = normalizedContentHash;
                var key = $"{normalizedFormat}|{normalizedContentHash}";
                var normalizedContentHashLower = normalizedContentHash.ToLowerInvariant();

                if (canonicalByHashAndFormat.TryGetValue(key, out var canonicalFromBatch))
                {
                    releaseDisc.Disc = canonicalFromBatch;
                    continue;
                }

                var canonicalFromLocal = this.GetDbContext().Discs.Local.FirstOrDefault(d =>
                    !ReferenceEquals(d, inputDisc) &&
                    !string.IsNullOrWhiteSpace(d.Format) &&
                    !string.IsNullOrWhiteSpace(d.ContentHash) &&
                    string.Equals(d.Format, normalizedFormat, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(d.ContentHash, normalizedContentHash, StringComparison.OrdinalIgnoreCase));

                if (canonicalFromLocal != null)
                {
                    this.CopyTitlesToCanonicalDiscIfMissing(canonicalFromLocal, inputDisc);
                    releaseDisc.Disc = canonicalFromLocal;
                    canonicalByHashAndFormat[key] = canonicalFromLocal;
                    continue;
                }

                var canonicalFromDbCandidates = await this.GetDbContext().Discs
                    .Include(d => d.Titles)
                    .ThenInclude(t => t.Item)
                    .ThenInclude(i => i.Chapters)
                    .Include(d => d.Titles)
                    .ThenInclude(t => t.Tracks)
                    .Where(
                        d => d.Format != null &&
                             d.ContentHash != null &&
                             d.ContentHash.ToLower() == normalizedContentHashLower)
                    .ToListAsync(cancellationToken);
                var canonicalFromDb = canonicalFromDbCandidates.FirstOrDefault(d =>
                    string.Equals(d.Format?.Trim(), normalizedFormat, StringComparison.OrdinalIgnoreCase));
                if (canonicalFromDb != null)
                {
                    this.CopyTitlesToCanonicalDiscIfMissing(canonicalFromDb, inputDisc);
                    releaseDisc.Disc = canonicalFromDb;
                    canonicalByHashAndFormat[key] = canonicalFromDb;
                    continue;
                }

                canonicalByHashAndFormat[key] = inputDisc;
            }
        }

        private void CopyTitlesToCanonicalDiscIfMissing(Disc canonicalDisc, Disc sourceDisc)
        {
            if (canonicalDisc.Titles.Count > 0 || sourceDisc.Titles.Count == 0)
            {
                return;
            }

            foreach (var sourceTitle in sourceDisc.Titles)
            {
                canonicalDisc.Titles.Add(sourceTitle);
            }
        }

        private async Task SaveChangesAsync()
        {
            try
            {
                await this.GetDbContext().SaveChangesAsync();
            }
            catch
            {
                // Detach pending entities so they don't pollute subsequent items.
                // After a failed SaveChanges, Added/Modified entities remain in the
                // change tracker. If the next item shares an entity (e.g. same
                // contributor), EF would try to insert duplicates.
                this.GetDbContext().ChangeTracker.Clear();
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
                            var existingContributor = await this.GetDbContext().Contributors
                                .FirstOrDefaultAsync(c => c.Name == contributor.Name, cancellationToken);
                            if (existingContributor != null)
                            {
                                seenContributors[contributor.Name] = existingContributor;
                                resolvedContributors.Add(existingContributor);
                            }
                            else
                            {
                                using var scope = this.scopeFactory.CreateScope();
                                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TheDiscDbUser>>();
                                var user = await userManager.FindByNameAsync(contributor.Name);
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

                    var existingContributor = await this.GetDbContext().Contributors
                        .FirstOrDefaultAsync(c => c.Name == contributor.Name, cancellationToken);
                    if (existingContributor != null)
                    {
                        seenContributors[contributor.Name] = existingContributor;
                        resolvedContributors.Add(existingContributor);
                    }
                    else
                    {
                        using var scope = this.scopeFactory.CreateScope();
                        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TheDiscDbUser>>();
                        var user = await userManager.FindByNameAsync(contributor.Name);
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
