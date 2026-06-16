namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class EditSuggestionServiceTests
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

    [Test]
    public async Task SubmitAsync_CreatesSuggestionWithChanges()
    {
        using var db = CreateDb();
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var service = new EditSuggestionService(db, factory, history);

        var proposed = JsonSerializer.Serialize(MakeReleaseDetails(), JsonOptions);
        var inputs = new List<SubmitChangeInput>
        {
            new(ReleaseFieldsUpdate.Key, proposed, OriginalSnapshotJson: null),
        };

        var result = await service.SubmitAsync("user-1", EditSuggestionSource.Web, "My edit", inputs, CT);

        await Assert.That(result.Id).IsGreaterThan(0);
        await Assert.That(result.Status).IsEqualTo(EditSuggestionStatus.Pending);
        await Assert.That(result.UserId).IsEqualTo("user-1");
        await Assert.That(result.Summary).IsEqualTo("My edit");
        await Assert.That(result.TargetEntityType).IsEqualTo("Release");
        await Assert.That(result.TargetEntityKey).IsEqualTo("the-movie/the-release");
        await Assert.That(result.Changes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SubmitAsync_ThrowsForUnknownTypeKey()
    {
        using var db = CreateDb();
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var service = new EditSuggestionService(db, factory, history);

        var inputs = new List<SubmitChangeInput>
        {
            new("bogus.type", "{}", null),
        };

        await Assert.That(async () => await service.SubmitAsync("user-1", EditSuggestionSource.Web, null, inputs, CT))
            .Throws<UnknownChangeTypeException>();
    }

    [Test]
    public async Task WithdrawAsync_TransitionsPendingToWithdrawn()
    {
        using var db = CreateDb();
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var service = new EditSuggestionService(db, factory, history);

        var proposed = JsonSerializer.Serialize(MakeReleaseDetails(), JsonOptions);
        var suggestion = await service.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);

        var result = await service.WithdrawAsync(suggestion.Id, "user-1", isAdmin: false, CT);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(EditSuggestionStatus.Withdrawn);
    }

    [Test]
    public async Task WithdrawAsync_ReturnsNullForNonOwnerNonAdmin()
    {
        using var db = CreateDb();
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var service = new EditSuggestionService(db, factory, history);

        var proposed = JsonSerializer.Serialize(MakeReleaseDetails(), JsonOptions);
        var suggestion = await service.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);

        var result = await service.WithdrawAsync(suggestion.Id, "user-2", isAdmin: false, CT);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task WithdrawAsync_AdminCanWithdrawAnyonesSuggestion()
    {
        using var db = CreateDb();
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var service = new EditSuggestionService(db, factory, history);

        var proposed = JsonSerializer.Serialize(MakeReleaseDetails(), JsonOptions);
        var suggestion = await service.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);

        var result = await service.WithdrawAsync(suggestion.Id, "admin-1", isAdmin: true, CT);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Status).IsEqualTo(EditSuggestionStatus.Withdrawn);
    }

    [Test]
    public async Task ListAsync_FiltersByUserId()
    {
        using var db = CreateDb();
        var factory = CreateFactory();
        var history = new EditSuggestionHistoryService(db);
        var service = new EditSuggestionService(db, factory, history);

        var proposed = JsonSerializer.Serialize(MakeReleaseDetails(), JsonOptions);
        await service.SubmitAsync("user-1", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);
        await service.SubmitAsync("user-2", EditSuggestionSource.Web, null,
            new List<SubmitChangeInput> { new(ReleaseFieldsUpdate.Key, proposed, null) }, CT);

        var user1Results = await service.ListAsync(new EditSuggestionListFilter
        {
            UserId = "user-1",
            MineOnly = true,
        }, CT);

        await Assert.That(user1Results.Count).IsEqualTo(1);
        await Assert.That(user1Results[0].UserId).IsEqualTo("user-1");
    }
}
