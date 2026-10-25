namespace TheDiscDb.UnitTests.Services.EditSuggestions;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.EditSuggestions;
using TheDiscDb.Web.Data;

public class EditSuggestionBulkReviewServiceTests
{
    [Test]
    public async Task Approve_UsesSubmissionOrderDeduplicatesAndUsesExistingAuditAndNotifications()
    {
        await using var harness = new Harness();
        var first = await harness.SeedAsync();
        var second = await harness.SeedAsync();

        var results = await harness.Bulk.ApproveAsync([second.Id, first.Id, second.Id], "admin-1");

        await Assert.That(results.Select(r => r.SuggestionId).ToArray())
            .IsEquivalentTo(new[] { first.Id, second.Id });
        await Assert.That(results[0].SuggestionId).IsEqualTo(first.Id);
        await Assert.That(results.All(r => r.Outcome == BulkReviewOutcome.Approved)).IsTrue();
        await Assert.That(results.All(r => r.Counts?.Applied == 1)).IsTrue();
        await using var db = harness.CreateDb();
        await Assert.That(await db.Releases.AllAsync(r => r.Title == "Updated")).IsTrue();
        await Assert.That(await db.Set<EditSuggestionHistory>().AllAsync(h => h.UserId == "admin-1")).IsTrue();
        await Assert.That(await db.Set<EditSuggestionHistory>().CountAsync()).IsGreaterThan(0);
        await Assert.That(harness.Notifications.Resolved.Count).IsEqualTo(2);
        await Assert.That(harness.Notifications.Resolved.SequenceEqual(new[] { first.Id, second.Id })).IsTrue();
        await Assert.That(harness.ContextIds.Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Approve_UsesTimestampBeforeIdAndBreaksTimestampTiesById()
    {
        await using var harness = new Harness();
        var newest = await harness.SeedAsync();
        var older = await harness.SeedAsync();
        var tied = await harness.SeedAsync();
        await using (var db = harness.CreateDb())
        {
            var timestamp = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
            (await db.EditSuggestions.FindAsync(newest.Id))!.Created = timestamp.AddHours(1);
            (await db.EditSuggestions.FindAsync(older.Id))!.Created = timestamp;
            (await db.EditSuggestions.FindAsync(tied.Id))!.Created = timestamp;
            await db.SaveChangesAsync();
        }

        var results = await harness.Bulk.ApproveAsync([newest.Id, tied.Id, older.Id], "admin-1");
        var expected = new[] { older.Id, tied.Id, newest.Id };

        await Assert.That(results.Select(r => r.SuggestionId).SequenceEqual(expected)).IsTrue();
        await Assert.That(harness.Notifications.Resolved.SequenceEqual(expected)).IsTrue();
        await Assert.That(results.All(r => r.Outcome == BulkReviewOutcome.Approved)).IsTrue();
    }

    [Test]
    public async Task Approve_AppliesDependentSuggestionsInSubmissionOrder()
    {
        await using var harness = new Harness();
        var older = await harness.SeedAsync();
        var newer = await harness.SeedAsync();
        await using (var db = harness.CreateDb())
        {
            var preceding = older.Changes.Single();
            var baseline = JsonSerializer.Deserialize<ReleaseFieldsDetails>(
                preceding.ProposedJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var next = await db.Set<EditSuggestionChange>().SingleAsync(c => c.SuggestionId == newer.Id);
            next.OriginalSnapshotJson = preceding.ProposedJson;
            next.ProposedJson = JsonSerializer.Serialize(
                baseline with { Title = "Final title" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await db.SaveChangesAsync();
        }

        var results = await harness.Bulk.ApproveAsync([newer.Id, older.Id], "admin-1");

        await Assert.That(results.All(r => r.Outcome == BulkReviewOutcome.Approved)).IsTrue();
        await using var verify = harness.CreateDb();
        await Assert.That(await verify.Releases.Where(r => r.Slug == $"release-{older.Id}")
            .Select(r => r.Title).SingleAsync()).IsEqualTo("Final title");
    }

    [Test]
    public async Task Reject_UsesSubmissionOrderRatherThanSelectionOrder()
    {
        await using var harness = new Harness();
        var older = await harness.SeedAsync();
        var newer = await harness.SeedAsync();

        var results = await harness.Bulk.RejectAsync([newer.Id, older.Id], "admin-1", "Incorrect");

        await Assert.That(results.Select(r => r.SuggestionId).SequenceEqual(new[] { older.Id, newer.Id })).IsTrue();
        await Assert.That(harness.Notifications.Resolved.SequenceEqual(new[] { older.Id, newer.Id })).IsTrue();
    }

    [Test]
    public async Task Reject_UsesSharedReasonAndDoesNotUndoAppliedDataOrCorrectTerminalSuggestions()
    {
        await using var harness = new Harness();
        var first = await harness.SeedAsync();
        var partial = await harness.SeedAsync(
            EditSuggestionChangeStatus.Applied, EditSuggestionChangeStatus.Conflicted);
        var terminal = await harness.SeedAsync(EditSuggestionChangeStatus.Applied);
        await using (var db = harness.CreateDb())
        {
            var applied = await db.EditSuggestions.FindAsync(partial.Id);
            applied!.Status = EditSuggestionStatus.PartiallyApproved;
            var approved = await db.EditSuggestions.FindAsync(terminal.Id);
            approved!.Status = EditSuggestionStatus.Approved;
            var release = await db.Releases.SingleAsync(r => r.Slug == $"release-{partial.Id}");
            release.Title = "Already applied";
            await db.SaveChangesAsync();
        }

        var results = await harness.Bulk.RejectAsync([first.Id, partial.Id, terminal.Id], "admin-2", " Incorrect ");

        await Assert.That(results[0].Outcome).IsEqualTo(BulkReviewOutcome.Rejected);
        await Assert.That(results[1].Outcome).IsEqualTo(BulkReviewOutcome.Rejected);
        await Assert.That(results[2].Outcome).IsEqualTo(BulkReviewOutcome.Skipped);
        await using var verify = harness.CreateDb();
        var rejected = await verify.EditSuggestions.Include(s => s.Changes)
            .Where(s => s.Id != terminal.Id).ToListAsync();
        await Assert.That(rejected.SelectMany(s => s.Changes)
            .All(c => c.Status == EditSuggestionChangeStatus.Rejected && c.AdminNote == "Incorrect")).IsTrue();
        await Assert.That(await verify.Releases.Where(r => r.Slug == $"release-{partial.Id}")
            .Select(r => r.Title).SingleAsync()).IsEqualTo("Already applied");
        await Assert.That(harness.Notifications.Resolved.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Approve_ProcessesOnlyPendingChildrenAndReportsConflicts()
    {
        await using var harness = new Harness();
        var suggestion = await harness.SeedAsync(
            EditSuggestionChangeStatus.Pending, EditSuggestionChangeStatus.Conflicted);

        var results = await harness.Bulk.ApproveAsync([suggestion.Id], "admin-1");

        await Assert.That(results[0].Outcome).IsEqualTo(BulkReviewOutcome.NeedsReview);
        await Assert.That(results[0].Counts!.Applied).IsEqualTo(1);
        await Assert.That(results[0].Counts!.Conflicted).IsEqualTo(1);
        await using var db = harness.CreateDb();
        var conflict = await db.Set<EditSuggestionChange>()
            .SingleAsync(c => c.SuggestionId == suggestion.Id && c.Ordinal == 1);
        await Assert.That(conflict.Status).IsEqualTo(EditSuggestionChangeStatus.Conflicted);
        await Assert.That(conflict.AdminNote).IsNull();
    }

    [Test]
    public async Task Approve_ValidationDriftIsAConflictNotSuccess()
    {
        await using var harness = new Harness();
        var suggestion = await harness.SeedAsync();
        await using (var db = harness.CreateDb())
        {
            (await db.Releases.SingleAsync()).Title = "Changed since submission";
            await db.SaveChangesAsync();
        }

        var results = await harness.Bulk.ApproveAsync([suggestion.Id], "admin-1");

        await Assert.That(results[0].Outcome).IsEqualTo(BulkReviewOutcome.NeedsReview);
        await Assert.That(results[0].Counts!.Applied).IsEqualTo(0);
        await Assert.That(results[0].Counts!.Conflicted).IsEqualTo(1);
    }

    [Test]
    public async Task Approve_SkipsStaleMissingAndZeroPendingSuggestions()
    {
        await using var harness = new Harness();
        List<int> ids = [];
        foreach (var status in new[]
            {
                EditSuggestionStatus.Draft, EditSuggestionStatus.Withdrawn,
                EditSuggestionStatus.Approved, EditSuggestionStatus.Rejected
            })
        {
            var suggestion = await harness.SeedAsync();
            ids.Add(suggestion.Id);
            await using var db = harness.CreateDb();
            (await db.EditSuggestions.FindAsync(suggestion.Id))!.Status = status;
            await db.SaveChangesAsync();
        }

        ids.Add((await harness.SeedAsync(EditSuggestionChangeStatus.Conflicted)).Id);
        ids.Add(int.MaxValue);

        var results = await harness.Bulk.ApproveAsync(ids, "admin-1");

        await Assert.That(results.All(r => r.Outcome == BulkReviewOutcome.Skipped)).IsTrue();
        await using var verify = harness.CreateDb();
        await Assert.That(await verify.Set<EditSuggestionHistory>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Failure_DoesNotLeakTrackedChangesOrStopTheNextSuggestion()
    {
        await using var harness = new Harness();
        var failing = await harness.SeedAsync(
            EditSuggestionChangeStatus.Pending, EditSuggestionChangeStatus.Pending);
        var next = await harness.SeedAsync();
        await using (var db = harness.CreateDb())
        {
            var change = await db.Set<EditSuggestionChange>()
                .SingleAsync(c => c.SuggestionId == failing.Id && c.Ordinal == 1);
            change.Type = "test.fail";
            await db.SaveChangesAsync();
        }

        var results = await harness.Bulk.ApproveAsync([failing.Id, next.Id], "admin-1");

        await Assert.That(results[0].Outcome).IsEqualTo(BulkReviewOutcome.Failed);
        await Assert.That(results[0].Counts).IsNull();
        await Assert.That(results[1].Outcome).IsEqualTo(BulkReviewOutcome.Approved);
        await using var verify = harness.CreateDb();
        var persisted = await verify.EditSuggestions.Include(s => s.Changes).SingleAsync(s => s.Id == failing.Id);
        await Assert.That(persisted.Changes.Single(c => c.Ordinal == 0).Status)
            .IsEqualTo(EditSuggestionChangeStatus.Applied);
        await Assert.That(persisted.Changes.Single(c => c.Ordinal == 1).Status)
            .IsEqualTo(EditSuggestionChangeStatus.Pending);
        await Assert.That(await verify.Releases.AllAsync(r => r.Title == "Updated")).IsTrue();
        await Assert.That(harness.ContextIds.Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Reject_RequiresAReasonBeforeAnyMutations()
    {
        await using var harness = new Harness();
        var suggestion = await harness.SeedAsync();

        await Assert.That(async () => { await harness.Bulk.RejectAsync([suggestion.Id], "admin-1", " "); })
            .Throws<ArgumentException>();
        await Assert.That(harness.ContextIds.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Cancellation_IsNotReportedAsAnItemFailure()
    {
        await using var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => { await harness.Bulk.ApproveAsync([1], "admin-1", cancellation.Token); })
            .Throws<OperationCanceledException>();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly DbContextOptions<SqlServerDataContext> options =
            new DbContextOptionsBuilder<SqlServerDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        private readonly ServiceProvider provider;
        public List<Guid> ContextIds { get; } = [];
        public RecordingNotifications Notifications { get; } = new();
        public EditSuggestionBulkReviewService Bulk { get; }

        public Harness()
        {
            var services = new ServiceCollection();
            services.AddScoped(_ =>
            {
                var db = CreateDb();
                ContextIds.Add(db.ContextId.InstanceId);
                return db;
            });
            services.AddSingleton<IChangeFactory>(new TestChangeFactory());
            services.AddScoped<IEditSuggestionHistoryService, EditSuggestionHistoryService>();
            services.AddScoped<IEditSuggestionReviewService>(p => new EditSuggestionReviewService(
                p.GetRequiredService<SqlServerDataContext>(),
                p.GetRequiredService<IChangeFactory>(),
                p.GetRequiredService<IEditSuggestionHistoryService>(),
                Notifications));
            provider = services.BuildServiceProvider();
            Bulk = new(provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<EditSuggestionBulkReviewService>.Instance);
        }

        public SqlServerDataContext CreateDb() => new(options);

        public async Task<EditSuggestion> SeedAsync(params EditSuggestionChangeStatus[] statuses)
        {
            await using var db = CreateDb();
            var number = await db.EditSuggestions.CountAsync() + 1;
            var slug = $"movie-{number}";
            var releaseSlug = $"release-{number}";
            var media = new MediaItem { Slug = slug, Title = slug, FullTitle = slug, Type = "movie", Year = 2020 };
            media.Releases.Add(new Release
            {
                Slug = releaseSlug,
                Title = "Original",
                RegionCode = "US",
                Locale = "en-US",
                Year = 2020,
                ReleaseDate = new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero),
                MediaItem = media
            });
            db.Add(media);
            var snapshot = new ReleaseFieldsDetails(slug, null, releaseSlug, "Original", "US", "en-US",
                2020, null, null, null, new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero));
            var suggestion = new EditSuggestion
            {
                UserId = "user-1",
                Created = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).AddSeconds(number),
                Summary = $"Edit {slug}",
                Status = EditSuggestionStatus.Pending
            };
            if (statuses.Length == 0)
            {
                statuses = [EditSuggestionChangeStatus.Pending];
            }

            foreach (var status in statuses)
            {
                suggestion.Changes.Add(new EditSuggestionChange
                {
                    Ordinal = suggestion.Changes.Count,
                    Type = ReleaseFieldsUpdate.Key,
                    Status = status,
                    OriginalSnapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    ProposedJson = JsonSerializer.Serialize(snapshot with { Title = "Updated" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                });
            }

            db.EditSuggestions.Add(suggestion);
            await db.SaveChangesAsync();
            return suggestion;
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private sealed class TestChangeFactory : IChangeFactory
    {
        private readonly ChangeFactory inner = new([
            new ChangeBuilder<ReleaseFieldsDetails>(ReleaseFieldsUpdate.Key, (details, options) => new ReleaseFieldsUpdate(details, options))
        ]);
        public IReadOnlyCollection<string> RegisteredTypeKeys => inner.RegisteredTypeKeys;
        public IChange Create(string typeKey, string proposedJson) =>
            typeKey == "test.fail" ? new FailingChange() : inner.Create(typeKey, proposedJson);
    }

    private sealed class FailingChange : IChange
    {
        public string TypeKey => "test.fail";
        public string TargetEntityKey => "movie-1/release-1";
        public Task<ChangeValidationResult> ValidateAsync(SqlServerDataContext context, string? originalSnapshotJson, CancellationToken cancellationToken) =>
            Task.FromResult(ChangeValidationResult.Ok());

        public async Task ApplyAsync(SqlServerDataContext context, IChangeApplyContext apply, CancellationToken cancellationToken)
        {
            (await context.Releases.SingleAsync(r => r.Slug == "release-1", cancellationToken)).Title = "Must not leak";
            throw new InvalidOperationException("Simulated failure after tracking a mutation.");
        }

        public Task<string?> GetCurrentSnapshotJsonAsync(SqlServerDataContext context, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class RecordingNotifications : IEditSuggestionNotificationService
    {
        public List<int> Resolved { get; } = [];
        public Task NotifySuggestionResolvedAsync(EditSuggestion suggestion, string? userEmail, CancellationToken cancellationToken = default)
        {
            Resolved.Add(suggestion.Id);
            return Task.CompletedTask;
        }

        public Task NotifySuggestionSubmittedAsync(EditSuggestion suggestion, string? userEmail, string? userName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyMessageFromUserAsync(EditSuggestion suggestion, string message, string? userName, string? userEmail, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyMessageFromAdminAsync(EditSuggestion suggestion, string message, string? userEmail, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}