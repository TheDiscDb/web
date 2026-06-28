namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Threading;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

// NOTE: MarkSyncedAsync uses ExecuteUpdateAsync, which the EF Core InMemory
// provider does not support, so its write behavior cannot be unit-tested with
// this harness (only the empty-list guard, which short-circuits before the
// update, is covered here). The ExecuteUpdate path needs integration coverage
// against a real relational provider. The GetUnsyncedChangesAsync query logic
// (filter + ordering + take) is fully covered below.
public class EditSuggestionSyncServiceTests
{
    private static readonly CancellationToken CT = CancellationToken.None;

    private static SqlServerDataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    private static async Task<EditSuggestionChange> SeedChangeAsync(
        SqlServerDataContext db,
        EditSuggestionChangeStatus status,
        DateTimeOffset? appliedAt = null,
        DateTimeOffset? syncedToFilesAt = null)
    {
        var suggestion = new EditSuggestion
        {
            UserId = "user-1",
            Created = DateTimeOffset.UtcNow,
            Status = EditSuggestionStatus.Pending,
            TargetEntityType = "Release",
            Source = EditSuggestionSource.Web,
        };

        var change = new EditSuggestionChange
        {
            Ordinal = 0,
            Type = "release.fields.update",
            ProposedJson = "{}",
            Status = status,
            AppliedAt = appliedAt,
            SyncedToFilesAt = syncedToFilesAt,
        };
        suggestion.Changes.Add(change);

        db.Add(suggestion);
        await db.SaveChangesAsync(CT);
        return change;
    }

    [Test]
    public async Task GetUnsyncedChangesAsync_ReturnsOnlyAppliedAndUnsynced()
    {
        var db = CreateDb();
        var applied = await SeedChangeAsync(db, EditSuggestionChangeStatus.Applied, appliedAt: DateTimeOffset.UtcNow);
        await SeedChangeAsync(db, EditSuggestionChangeStatus.Pending);
        await SeedChangeAsync(db, EditSuggestionChangeStatus.Rejected);
        await SeedChangeAsync(db, EditSuggestionChangeStatus.Conflicted);

        var service = new EditSuggestionSyncService(db);
        var result = await service.GetUnsyncedChangesAsync(100, CT);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Id).IsEqualTo(applied.Id);
    }

    [Test]
    public async Task GetUnsyncedChangesAsync_ExcludesAlreadySyncedChanges()
    {
        var db = CreateDb();
        await SeedChangeAsync(db, EditSuggestionChangeStatus.Applied,
            appliedAt: DateTimeOffset.UtcNow, syncedToFilesAt: DateTimeOffset.UtcNow);

        var service = new EditSuggestionSyncService(db);
        var result = await service.GetUnsyncedChangesAsync(100, CT);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetUnsyncedChangesAsync_OrdersByAppliedAtAscending()
    {
        var db = CreateDb();
        var newest = await SeedChangeAsync(db, EditSuggestionChangeStatus.Applied,
            appliedAt: new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var oldest = await SeedChangeAsync(db, EditSuggestionChangeStatus.Applied,
            appliedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var middle = await SeedChangeAsync(db, EditSuggestionChangeStatus.Applied,
            appliedAt: new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var service = new EditSuggestionSyncService(db);
        var result = await service.GetUnsyncedChangesAsync(100, CT);

        await Assert.That(result.Select(c => c.Id).ToList())
            .IsEquivalentTo(new[] { oldest.Id, middle.Id, newest.Id });
    }

    [Test]
    public async Task GetUnsyncedChangesAsync_RespectsTakeLimit()
    {
        var db = CreateDb();
        for (var i = 0; i < 5; i++)
        {
            await SeedChangeAsync(db, EditSuggestionChangeStatus.Applied,
                appliedAt: new DateTimeOffset(2024, 1, 1 + i, 0, 0, 0, TimeSpan.Zero));
        }

        var service = new EditSuggestionSyncService(db);
        var result = await service.GetUnsyncedChangesAsync(2, CT);

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MarkSyncedAsync_IsNoOpForEmptyList()
    {
        var db = CreateDb();
        var change = await SeedChangeAsync(db, EditSuggestionChangeStatus.Applied, appliedAt: DateTimeOffset.UtcNow);

        var service = new EditSuggestionSyncService(db);
        await service.MarkSyncedAsync(Array.Empty<int>(), CT);

        var reloaded = await db.EditSuggestionChanges.AsNoTracking().FirstAsync(c => c.Id == change.Id, CT);
        await Assert.That(reloaded.SyncedToFilesAt).IsNull();
    }
}
