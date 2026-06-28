namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Threading;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class EditSuggestionHistoryServiceTests
{
    private static readonly CancellationToken CT = CancellationToken.None;
    private const int SuggestionId = 42;

    private static SqlServerDataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    [Test]
    public async Task RecordCreatedAsync_WritesCreatedEntry()
    {
        using var db = CreateDb();
        var history = new EditSuggestionHistoryService(db);

        await history.RecordCreatedAsync(SuggestionId, "user-1", CT);

        var entry = await db.EditSuggestionHistory.SingleAsync();
        await Assert.That(entry.SuggestionId).IsEqualTo(SuggestionId);
        await Assert.That(entry.Type).IsEqualTo(EditSuggestionHistoryType.Created);
        await Assert.That(entry.UserId).IsEqualTo("user-1");
    }

    [Test]
    public async Task RecordStatusChangedAsync_DescribesTransition()
    {
        using var db = CreateDb();
        var history = new EditSuggestionHistoryService(db);

        await history.RecordStatusChangedAsync(SuggestionId, "admin-1",
            EditSuggestionStatus.Pending, EditSuggestionStatus.Approved, CT);

        var entry = await db.EditSuggestionHistory.SingleAsync();
        await Assert.That(entry.Type).IsEqualTo(EditSuggestionHistoryType.StatusChanged);
        await Assert.That(entry.Description).Contains("Pending");
        await Assert.That(entry.Description).Contains("Approved");
    }

    [Test]
    public async Task RecordChangeStatusChangedAsync_IncludesChangeIdAndAdminNote()
    {
        using var db = CreateDb();
        var history = new EditSuggestionHistoryService(db);

        await history.RecordChangeStatusChangedAsync(SuggestionId, changeId: 7, "admin-1",
            EditSuggestionChangeStatus.Pending, EditSuggestionChangeStatus.Rejected, adminNote: "Not accurate", CT);

        var entry = await db.EditSuggestionHistory.SingleAsync();
        await Assert.That(entry.Type).IsEqualTo(EditSuggestionHistoryType.ChangeStatusChanged);
        await Assert.That(entry.ChangeId).IsEqualTo(7);
        await Assert.That(entry.Description).Contains("Not accurate");
    }

    [Test]
    public async Task RecordWithdrawnAsync_WritesWithdrawnEntry()
    {
        using var db = CreateDb();
        var history = new EditSuggestionHistoryService(db);

        await history.RecordWithdrawnAsync(SuggestionId, "user-1", CT);

        var entry = await db.EditSuggestionHistory.SingleAsync();
        await Assert.That(entry.Type).IsEqualTo(EditSuggestionHistoryType.Withdrawn);
    }

    [Test]
    public async Task RecordMessageAsync_UsesProvidedMessageType()
    {
        using var db = CreateDb();
        var history = new EditSuggestionHistoryService(db);

        await history.RecordMessageAsync(SuggestionId, "admin-1", EditSuggestionHistoryType.AdminMessage, CT);

        var entry = await db.EditSuggestionHistory.SingleAsync();
        await Assert.That(entry.Type).IsEqualTo(EditSuggestionHistoryType.AdminMessage);
        await Assert.That(entry.Description).Contains("Admin message");
    }
}
