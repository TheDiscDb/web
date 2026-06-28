namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class EditSuggestionMessagingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CancellationToken CT = CancellationToken.None;

    private static SqlServerDataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    private static IChangeFactory CreateFactory()
    {
        var builders = new IChangeBuilder[]
        {
            new ChangeBuilder<ReleaseFieldsDetails>(
                ReleaseFieldsUpdate.Key,
                (d, opts) => new ReleaseFieldsUpdate(d, opts)),
        };
        return new ChangeFactory(builders);
    }

    private static ReleaseFieldsDetails MakeReleaseDetails() => new(
        MediaItemSlug: "the-movie",
        BoxsetSlug: null,
        ReleaseSlug: "the-release",
        Title: "Updated Title",
        RegionCode: "US",
        Locale: "en-US",
        Year: 2020,
        Upc: null,
        Isbn: null,
        Asin: null,
        ReleaseDate: new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero));

    private static async Task<(EditSuggestionService Service, SqlServerDataContext Db, int SuggestionId)> SeedSuggestionAsync(string ownerId = "user-1")
    {
        var db = CreateDb();
        var service = new EditSuggestionService(db, CreateFactory(), new EditSuggestionHistoryService(db));
        var proposed = JsonSerializer.Serialize(MakeReleaseDetails(), JsonOptions);
        var suggestion = await service.SubmitAsync(ownerId, EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);
        return (service, db, suggestion.Id);
    }

    [Test]
    public async Task AddMessageAsync_OwnerCanPost_PersistsMessageAndRecordsUserMessageHistory()
    {
        var (service, db, suggestionId) = await SeedSuggestionAsync();
        using var _ = db;

        var message = await service.AddMessageAsync(suggestionId, "user-1", "admin-1", "Please clarify", isAdmin: false, CT);

        await Assert.That(message.SuggestionId).IsEqualTo(suggestionId);
        await Assert.That(message.FromUserId).IsEqualTo("user-1");
        await Assert.That(message.ToUserId).IsEqualTo("admin-1");
        await Assert.That(message.Message).IsEqualTo("Please clarify");

        var persisted = await db.EditSuggestionMessages.SingleAsync(m => m.SuggestionId == suggestionId);
        await Assert.That(persisted.Message).IsEqualTo("Please clarify");

        var history = await db.EditSuggestionHistory
            .Where(h => h.SuggestionId == suggestionId && h.Type == EditSuggestionHistoryType.UserMessage)
            .SingleAsync();
        await Assert.That(history.UserId).IsEqualTo("user-1");
    }

    [Test]
    public async Task AddMessageAsync_AdminCanPostToAnySuggestion_RecordsAdminMessageHistory()
    {
        var (service, db, suggestionId) = await SeedSuggestionAsync();
        using var _ = db;

        var message = await service.AddMessageAsync(suggestionId, "admin-9", "user-1", "Looks good", isAdmin: true, CT);

        await Assert.That(message.FromUserId).IsEqualTo("admin-9");

        var history = await db.EditSuggestionHistory
            .Where(h => h.SuggestionId == suggestionId && h.Type == EditSuggestionHistoryType.AdminMessage)
            .SingleAsync();
        await Assert.That(history.UserId).IsEqualTo("admin-9");
    }

    [Test]
    public async Task AddMessageAsync_NonOwnerNonAdmin_Throws()
    {
        var (service, db, suggestionId) = await SeedSuggestionAsync();
        using var _ = db;

        await Assert.That(async () =>
            await service.AddMessageAsync(suggestionId, "intruder", "user-1", "hi", isAdmin: false, CT))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task AddMessageAsync_UnknownSuggestion_Throws()
    {
        using var db = CreateDb();
        var service = new EditSuggestionService(db, CreateFactory(), new EditSuggestionHistoryService(db));

        await Assert.That(async () =>
            await service.AddMessageAsync(9999, "user-1", "admin-1", "hi", isAdmin: false, CT))
            .Throws<InvalidOperationException>();
    }
}
