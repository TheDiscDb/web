using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.Server.Services;

public class ContributionHistoryServiceTests
{
    private static SqlServerDataContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    [Test]
    public async Task RecordCreatedAsync_AddsHistoryEntry()
    {
        using var db = CreateDbContext();
        var service = new ContributionHistoryService(db);

        await service.RecordCreatedAsync(42, "user-1");

        var entries = await db.ContributionHistory.ToListAsync();
        await Assert.That(entries).HasCount().EqualTo(1);

        var entry = entries[0];
        await Assert.That(entry.ContributionId).IsEqualTo(42);
        await Assert.That(entry.UserId).IsEqualTo("user-1");
        await Assert.That(entry.Type).IsEqualTo(ContributionHistoryType.Created);
        await Assert.That(entry.Description).IsEqualTo("Contribution created");
        await Assert.That(entry.TimeStamp).IsNotNull();
    }

    [Test]
    public async Task RecordStatusChangedAsync_AddsHistoryWithStatusDescription()
    {
        using var db = CreateDbContext();
        var service = new ContributionHistoryService(db);

        await service.RecordStatusChangedAsync(42, "user-1", UserContributionStatus.Pending, UserContributionStatus.Approved);

        var entries = await db.ContributionHistory.ToListAsync();
        await Assert.That(entries).HasCount().EqualTo(1);

        var entry = entries[0];
        await Assert.That(entry.ContributionId).IsEqualTo(42);
        await Assert.That(entry.Type).IsEqualTo(ContributionHistoryType.StatusChanged);
        await Assert.That(entry.Description).IsEqualTo("Status changed from **Pending** to **Approved**");
    }

    [Test]
    public async Task RecordDeletedAsync_AddsHistoryEntry()
    {
        using var db = CreateDbContext();
        var service = new ContributionHistoryService(db);

        await service.RecordDeletedAsync(42, "user-1");

        var entries = await db.ContributionHistory.ToListAsync();
        await Assert.That(entries).HasCount().EqualTo(1);

        var entry = entries[0];
        await Assert.That(entry.ContributionId).IsEqualTo(42);
        await Assert.That(entry.Type).IsEqualTo(ContributionHistoryType.Deleted);
        await Assert.That(entry.Description).IsEqualTo("Contribution deleted");
    }

    [Test]
    public async Task RecordCreatedAsync_SetsTimestampCloseToNow()
    {
        using var db = CreateDbContext();
        var service = new ContributionHistoryService(db);

        var before = DateTimeOffset.UtcNow;
        await service.RecordCreatedAsync(1, "user-1");
        var after = DateTimeOffset.UtcNow;

        var entry = await db.ContributionHistory.SingleAsync();
        await Assert.That(entry.TimeStamp).IsGreaterThanOrEqualTo(before);
        await Assert.That(entry.TimeStamp).IsLessThanOrEqualTo(after);
    }

    [Test]
    public async Task MultipleRecords_AreIndependent()
    {
        using var db = CreateDbContext();
        var service = new ContributionHistoryService(db);

        await service.RecordCreatedAsync(1, "user-1");
        await service.RecordStatusChangedAsync(1, "user-1", UserContributionStatus.Pending, UserContributionStatus.ReadyForReview);
        await service.RecordDeletedAsync(2, "user-2");

        var entries = await db.ContributionHistory.ToListAsync();
        await Assert.That(entries).HasCount().EqualTo(3);
        await Assert.That(entries.Count(e => e.ContributionId == 1)).IsEqualTo(2);
        await Assert.That(entries.Count(e => e.ContributionId == 2)).IsEqualTo(1);
    }
}
