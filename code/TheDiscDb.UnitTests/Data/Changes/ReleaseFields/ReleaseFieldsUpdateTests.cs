namespace TheDiscDb.UnitTests.Data.Changes.ReleaseFields;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public class ReleaseFieldsUpdateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static SqlServerDataContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    private static Release SeedRelease(SqlServerDataContext db, int id = 1)
    {
        var release = new Release
        {
            Id = id,
            Slug = "the-release-slug",
            Title = "Original Title",
            RegionCode = "US",
            Locale = "en-US",
            Year = 2020,
            Upc = "123456789012",
            Isbn = null,
            Asin = "B0001234",
            ReleaseDate = new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero),
        };
        db.Releases.Add(release);
        db.SaveChanges();
        return release;
    }

    private sealed record TestApplyContext(
        string ApprovingUserId,
        int SuggestionId,
        int ChangeId,
        string? OriginalSnapshotJson = null) : IChangeApplyContext;

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenReleaseDoesNotExist()
    {
        using var db = CreateDbContext();
        var change = new ReleaseFieldsUpdate(new ReleaseFieldsDetails(
            ReleaseId: 999, Title: "x", RegionCode: "US", Locale: "en", Year: 2020,
            Upc: null, Isbn: null, Asin: null, ReleaseDate: DateTimeOffset.UnixEpoch));

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("999");
    }

    [Test]
    public async Task ValidateAsync_ReturnsOk_WhenSnapshotMatchesCurrentAndProposedDiffers()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshot = JsonSerializer.Serialize(ReleaseFieldsUpdate.SnapshotFrom(release), JsonOptions);

        var change = new ReleaseFieldsUpdate(new ReleaseFieldsDetails(
            ReleaseId: release.Id, Title: "Updated Title", RegionCode: release.RegionCode,
            Locale: release.Locale, Year: release.Year, Upc: release.Upc, Isbn: release.Isbn,
            Asin: release.Asin, ReleaseDate: release.ReleaseDate));

        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
        await Assert.That(result.IsNoOp).IsFalse();
    }

    [Test]
    public async Task ValidateAsync_ReturnsNoOp_WhenProposedMatchesCurrent()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshot = JsonSerializer.Serialize(ReleaseFieldsUpdate.SnapshotFrom(release), JsonOptions);

        // Proposed identical to current — nothing to apply.
        var change = new ReleaseFieldsUpdate(ReleaseFieldsUpdate.SnapshotFrom(release));

        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
        await Assert.That(result.IsNoOp).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenSnapshotDriftsFromCurrent()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);

        // User submitted with a stale snapshot — Title differs from current.
        var staleSnapshot = JsonSerializer.Serialize(new ReleaseFieldsDetails(
            ReleaseId: release.Id, Title: "An Older Title That No Longer Matches",
            RegionCode: release.RegionCode, Locale: release.Locale, Year: release.Year,
            Upc: release.Upc, Isbn: release.Isbn, Asin: release.Asin,
            ReleaseDate: release.ReleaseDate), JsonOptions);

        var change = new ReleaseFieldsUpdate(new ReleaseFieldsDetails(
            ReleaseId: release.Id, Title: "Proposed Title", RegionCode: release.RegionCode,
            Locale: release.Locale, Year: release.Year, Upc: release.Upc, Isbn: release.Isbn,
            Asin: release.Asin, ReleaseDate: release.ReleaseDate));

        var result = await change.ValidateAsync(db, staleSnapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("Title");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_OnMalformedSnapshotJson()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);

        var change = new ReleaseFieldsUpdate(ReleaseFieldsUpdate.SnapshotFrom(release) with { Title = "new" });
        var result = await change.ValidateAsync(db, "{ not valid", CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ApplyAsync_UpdatesAllEditableFields_AndDoesNotTouchSlug()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var originalSlug = release.Slug;
        var newDate = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshot = JsonSerializer.Serialize(ReleaseFieldsUpdate.SnapshotFrom(release), JsonOptions);

        var proposed = new ReleaseFieldsDetails(
            ReleaseId: release.Id,
            Title: "Brand New Title",
            RegionCode: "GB",
            Locale: "en-GB",
            Year: 2025,
            Upc: "999888777666",
            Isbn: "978-3-16-148410-0",
            Asin: "B0009999",
            ReleaseDate: newDate);

        var change = new ReleaseFieldsUpdate(proposed);
        await change.ApplyAsync(
            db,
            new TestApplyContext("admin-user", SuggestionId: 1, ChangeId: 1, OriginalSnapshotJson: snapshot),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Releases.FirstAsync(r => r.Id == release.Id);
        await Assert.That(reloaded.Title).IsEqualTo("Brand New Title");
        await Assert.That(reloaded.RegionCode).IsEqualTo("GB");
        await Assert.That(reloaded.Locale).IsEqualTo("en-GB");
        await Assert.That(reloaded.Year).IsEqualTo(2025);
        await Assert.That(reloaded.Upc).IsEqualTo("999888777666");
        await Assert.That(reloaded.Isbn).IsEqualTo("978-3-16-148410-0");
        await Assert.That(reloaded.Asin).IsEqualTo("B0009999");
        await Assert.That(reloaded.ReleaseDate).IsEqualTo(newDate);
        // Slug must be untouched — it appears in public URLs.
        await Assert.That(reloaded.Slug).IsEqualTo(originalSlug);
    }

    [Test]
    public async Task ApplyAsync_Throws_WhenReleaseRemovedBetweenValidateAndApply()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshot = JsonSerializer.Serialize(ReleaseFieldsUpdate.SnapshotFrom(release), JsonOptions);
        var change = new ReleaseFieldsUpdate(ReleaseFieldsUpdate.SnapshotFrom(release) with { Title = "x" });

        db.Releases.Remove(release);
        await db.SaveChangesAsync();

        // Apply re-validates inside the call; missing target now surfaces as ChangeApplyConflictException
        // rather than a raw InvalidOperationException.
        var ex = await Assert.ThrowsAsync<ChangeApplyConflictException>(
            () => change.ApplyAsync(
                db,
                new TestApplyContext("admin", 1, 1, OriginalSnapshotJson: snapshot),
                CancellationToken.None));
        await Assert.That(ex!.TypeKey).IsEqualTo(ReleaseFieldsUpdate.Key);
    }

    [Test]
    public async Task ApplyAsync_Throws_WhenSnapshotDriftsBetweenValidateAndApply()
    {
        // TOCTOU coverage: the change is built and revalidated against the snapshot inside Apply.
        // If the row changed since the snapshot was captured, Apply must surface a conflict
        // rather than silently overwriting newer data.
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var staleSnapshot = JsonSerializer.Serialize(
            ReleaseFieldsUpdate.SnapshotFrom(release) with { Title = "Title Nobody Currently Sees" },
            JsonOptions);

        var change = new ReleaseFieldsUpdate(ReleaseFieldsUpdate.SnapshotFrom(release) with { Title = "Proposed" });

        var ex = await Assert.ThrowsAsync<ChangeApplyConflictException>(
            () => change.ApplyAsync(
                db,
                new TestApplyContext("admin", 1, 1, OriginalSnapshotJson: staleSnapshot),
                CancellationToken.None));
        await Assert.That(ex!.Reason).Contains("Title");
    }

    [Test]
    public async Task ApplyAsync_Throws_WhenOriginalSnapshotMissing()
    {
        // Null snapshot on an update-type change must be rejected, not silently treated as a blind write.
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var change = new ReleaseFieldsUpdate(ReleaseFieldsUpdate.SnapshotFrom(release) with { Title = "x" });

        var ex = await Assert.ThrowsAsync<ChangeApplyConflictException>(
            () => change.ApplyAsync(
                db,
                new TestApplyContext("admin", 1, 1, OriginalSnapshotJson: null),
                CancellationToken.None));
        await Assert.That(ex!.Reason).Contains("Original snapshot");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenOriginalSnapshotMissing()
    {
        // Companion to ApplyAsync_Throws_WhenOriginalSnapshotMissing — validate stage rejects too.
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var change = new ReleaseFieldsUpdate(ReleaseFieldsUpdate.SnapshotFrom(release) with { Title = "x" });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("Original snapshot");
    }

    [Test]
    [Arguments("Title")]
    [Arguments("RegionCode")]
    [Arguments("Locale")]
    [Arguments("Year")]
    [Arguments("Upc")]
    [Arguments("Isbn")]
    [Arguments("Asin")]
    [Arguments("ReleaseDate")]
    public async Task ValidateAsync_DetectsDriftOnAnyEditableField(string fieldName)
    {
        // Parameterised coverage: any single field that differs between the original
        // snapshot the user submitted and the current database row must be flagged.
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var current = ReleaseFieldsUpdate.SnapshotFrom(release);
        var staleSnapshot = JsonSerializer.Serialize(DriftField(current, fieldName), JsonOptions);

        var change = new ReleaseFieldsUpdate(current with { Title = "Proposed Title" });

        var result = await change.ValidateAsync(db, staleSnapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains(fieldName);
    }

    private static ReleaseFieldsDetails DriftField(ReleaseFieldsDetails baseline, string fieldName) => fieldName switch
    {
        "Title" => baseline with { Title = "DRIFTED " + baseline.Title },
        "RegionCode" => baseline with { RegionCode = baseline.RegionCode == "GB" ? "US" : "GB" },
        "Locale" => baseline with { Locale = "fr-CA" },
        "Year" => baseline with { Year = baseline.Year + 1 },
        "Upc" => baseline with { Upc = "000000000000" },
        "Isbn" => baseline with { Isbn = "978-3-16-148410-0" },
        "Asin" => baseline with { Asin = "B0009999" },
        "ReleaseDate" => baseline with { ReleaseDate = baseline.ReleaseDate.AddYears(-5) },
        _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, "Unhandled field."),
    };

    [Test]
    public async Task TypeKey_MatchesConstant()
    {
        var change = new ReleaseFieldsUpdate(new ReleaseFieldsDetails(
            ReleaseId: 1, Title: null, RegionCode: null, Locale: null, Year: 0,
            Upc: null, Isbn: null, Asin: null, ReleaseDate: DateTimeOffset.UnixEpoch));
        await Assert.That(change.TypeKey).IsEqualTo("release.fields.update");
        await Assert.That(change.TypeKey).IsEqualTo(ReleaseFieldsUpdate.Key);
    }
}
