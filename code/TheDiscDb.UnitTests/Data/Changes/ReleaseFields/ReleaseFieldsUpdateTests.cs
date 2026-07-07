namespace TheDiscDb.UnitTests.Data.Changes.ReleaseFields;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Changes;
using TheDiscDb.Data.Changes.ReleaseFields;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public class ReleaseFieldsUpdateTests
{
    private const string MediaItemSlug = "the-movie";
    private const string ReleaseSlug = "the-release-slug";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static SqlServerDataContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }

    /// <summary>
    /// Seeds a MediaItem + Release pair so the natural-key composite
    /// (MediaItemSlug, ReleaseSlug) can resolve back to the release.
    /// Int ids are deliberately NOT used to identify the release in any change —
    /// the whole point of this design is that those ids drift on data rebuild.
    /// </summary>
    private static Release SeedRelease(SqlServerDataContext db)
    {
        var mediaItem = new MediaItem
        {
            Slug = MediaItemSlug,
            Title = "The Movie",
            FullTitle = "The Movie (2020)",
            Year = 2020,
            Type = "movie",
        };

        var release = new Release
        {
            Slug = ReleaseSlug,
            Title = "Original Title",
            RegionCode = "US",
            Locale = "en-US",
            Year = 2020,
            Upc = "123456789012",
            Isbn = null,
            Asin = "B0001234",
            ReleaseDate = new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero),
            MediaItem = mediaItem,
        };

        mediaItem.Releases.Add(release);
        db.Add(mediaItem);
        db.SaveChanges();
        return release;
    }

    private static ReleaseFieldsDetails MakeProposed(
        string? title = "Original Title",
        string? regionCode = "US",
        string? locale = "en-US",
        int year = 2020,
        string? upc = "123456789012",
        string? isbn = null,
        string? asin = "B0001234",
        DateTimeOffset? releaseDate = null)
        => new(
            MediaItemSlug: MediaItemSlug,
            BoxsetSlug: null,
            ReleaseSlug: ReleaseSlug,
            Title: title,
            RegionCode: regionCode,
            Locale: locale,
            Year: year,
            Upc: upc,
            Isbn: isbn,
            Asin: asin,
            ReleaseDate: releaseDate ?? new DateTimeOffset(2020, 5, 15, 0, 0, 0, TimeSpan.Zero));

    private sealed record TestApplyContext(
        string ApprovingUserId,
        int SuggestionId,
        int ChangeId,
        string? OriginalSnapshotJson = null) : IChangeApplyContext;

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenReleaseSlugPairDoesNotResolve()
    {
        using var db = CreateDbContext();
        SeedRelease(db);

        // Wrong parent slug — release exists but not under this MediaItem.
        var change = new ReleaseFieldsUpdate(MakeProposed(title: "x") with { MediaItemSlug = "not-the-movie" });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("not-the-movie");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenNeitherParentSlugSupplied()
    {
        // Caller-error guard: a release belongs to either a MediaItem or a Boxset,
        // exactly one must be supplied. Submitting neither surfaces as a Conflict
        // (queue-resolvable) rather than a crash.
        using var db = CreateDbContext();
        SeedRelease(db);

        var change = new ReleaseFieldsUpdate(MakeProposed(title: "x") with { MediaItemSlug = null, BoxsetSlug = null });

        var result = await change.ValidateAsync(db, originalSnapshotJson: null, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_ReturnsOk_WhenSnapshotMatchesCurrentAndProposedDiffers()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshot = JsonSerializer.Serialize(
            ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null),
            JsonOptions);

        var change = new ReleaseFieldsUpdate(MakeProposed(title: "Updated Title"));

        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
        await Assert.That(result.IsNoOp).IsFalse();
    }

    [Test]
    public async Task ValidateAsync_ReturnsNoOp_WhenProposedMatchesCurrent()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshotPayload = ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var change = new ReleaseFieldsUpdate(snapshotPayload);

        var result = await change.ValidateAsync(db, snapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
        await Assert.That(result.IsNoOp).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_WhenSnapshotDriftsFromCurrent()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);

        // Stale snapshot Title disagrees with current value.
        var staleSnapshot = JsonSerializer.Serialize(
            ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null) with { Title = "An Older Title That No Longer Matches" },
            JsonOptions);

        var change = new ReleaseFieldsUpdate(MakeProposed(title: "Proposed Title"));

        var result = await change.ValidateAsync(db, staleSnapshot, CancellationToken.None);

        await Assert.That(result.IsConflict).IsTrue();
        await Assert.That(result.ConflictReason).Contains("Title");
    }

    [Test]
    public async Task ValidateAsync_ReturnsConflict_OnMalformedSnapshotJson()
    {
        using var db = CreateDbContext();
        SeedRelease(db);

        var change = new ReleaseFieldsUpdate(MakeProposed(title: "new"));
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
        var snapshotPayload = ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var proposed = MakeProposed(
            title: "Brand New Title",
            regionCode: "GB",
            locale: "en-GB",
            year: 2025,
            upc: "999888777666",
            isbn: "978-3-16-148410-0",
            asin: "B0009999",
            releaseDate: newDate);

        var change = new ReleaseFieldsUpdate(proposed);
        await change.ApplyAsync(
            db,
            new TestApplyContext("admin-user", SuggestionId: 1, ChangeId: 1, OriginalSnapshotJson: snapshot),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Releases.FirstAsync(r => r.Slug == ReleaseSlug);
        await Assert.That(reloaded.Title).IsEqualTo("Brand New Title");
        await Assert.That(reloaded.RegionCode).IsEqualTo("GB");
        await Assert.That(reloaded.Locale).IsEqualTo("en-GB");
        await Assert.That(reloaded.Year).IsEqualTo(2025);
        await Assert.That(reloaded.Upc).IsEqualTo("999888777666");
        await Assert.That(reloaded.Isbn).IsEqualTo("978-3-16-148410-0");
        await Assert.That(reloaded.Asin).IsEqualTo("B0009999");
        await Assert.That(reloaded.ReleaseDate).IsEqualTo(newDate);
        // Slug must be untouched — it appears in public URLs AND is part of the
        // natural key the suggestion uses to find this row on later rebuilds.
        await Assert.That(reloaded.Slug).IsEqualTo(originalSlug);
    }

    [Test]
    public async Task ApplyAsync_OnlyWritesChangedFields_LeavingOthersUntouched()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshotPayload = ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        // User only changed Title; every other field matches the snapshot value.
        var proposed = snapshotPayload with { Title = "Only Title Changed" };
        var change = new ReleaseFieldsUpdate(proposed);

        await change.ApplyAsync(
            db,
            new TestApplyContext("admin", 1, 1, OriginalSnapshotJson: snapshot),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Releases.FirstAsync(r => r.Slug == ReleaseSlug);
        await Assert.That(reloaded.Title).IsEqualTo("Only Title Changed");
        // All other fields preserved at their original values.
        await Assert.That(reloaded.RegionCode).IsEqualTo("US");
        await Assert.That(reloaded.Locale).IsEqualTo("en-US");
        await Assert.That(reloaded.Year).IsEqualTo(2020);
        await Assert.That(reloaded.Upc).IsEqualTo("123456789012");
        await Assert.That(reloaded.Isbn).IsNull();
        await Assert.That(reloaded.Asin).IsEqualTo("B0001234");
    }

    [Test]
    public async Task ApplyAsync_ExplicitClear_IsApplied()
    {
        // Counterpart to the previous test: when snapshot.Field was non-null and
        // proposed.Field is null, that IS a deliberate clear and must be written.
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshotPayload = ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var proposed = snapshotPayload with { Asin = null };
        var change = new ReleaseFieldsUpdate(proposed);

        await change.ApplyAsync(
            db,
            new TestApplyContext("admin", 1, 1, OriginalSnapshotJson: snapshot),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Releases.FirstAsync(r => r.Slug == ReleaseSlug);
        await Assert.That(reloaded.Asin).IsNull();
        await Assert.That(reloaded.Title).IsEqualTo("Original Title");
        await Assert.That(reloaded.Upc).IsEqualTo("123456789012");
    }

    [Test]
    public async Task ApplyAsync_NoOp_WhenProposedExactlyMatchesSnapshot()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshotPayload = ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null);
        var snapshot = JsonSerializer.Serialize(snapshotPayload, JsonOptions);

        var change = new ReleaseFieldsUpdate(snapshotPayload);
        await change.ApplyAsync(
            db,
            new TestApplyContext("admin", 1, 1, OriginalSnapshotJson: snapshot),
            CancellationToken.None);

        await Assert.That(db.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    public async Task ApplyAsync_Throws_WhenReleaseRemovedBetweenValidateAndApply()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var snapshot = JsonSerializer.Serialize(
            ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null),
            JsonOptions);
        var change = new ReleaseFieldsUpdate(MakeProposed(title: "x"));

        db.Releases.Remove(release);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ChangeApplyConflictException>(
            () => change.ApplyAsync(
                db,
                new TestApplyContext("admin", 1, 1, OriginalSnapshotJson: snapshot),
                CancellationToken.None));
        await Assert.That(ex!.TypeKey).IsEqualTo(ReleaseFieldsUpdate.Key);
    }

    [Test]
    public async Task ApplyAsync_StillResolves_AfterDataRebuildShiftsIntIds()
    {
        // The whole point of natural-key identity: simulate a "rebuild" where the
        // int id of the release changes (e.g. the data tables were truncated and
        // reseeded) but the slug pair is stable. The change submitted before the
        // rebuild must still resolve against the new row.
        using var db = CreateDbContext();
        var releaseBefore = SeedRelease(db);
        var snapshot = JsonSerializer.Serialize(
            ReleaseFieldsUpdate.SnapshotFrom(releaseBefore, MediaItemSlug, null),
            JsonOptions);

        // Simulate rebuild: drop & re-add the MediaItem+Release with the same slugs
        // but a fresh identity in the in-memory store.
        db.RemoveRange(db.Releases);
        db.RemoveRange(db.MediaItems);
        await db.SaveChangesAsync();
        SeedRelease(db);

        var change = new ReleaseFieldsUpdate(MakeProposed(title: "After-Rebuild Edit"));
        await change.ApplyAsync(
            db,
            new TestApplyContext("admin", 1, 1, OriginalSnapshotJson: snapshot),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var reloaded = await db.Releases.FirstAsync(r => r.Slug == ReleaseSlug);
        await Assert.That(reloaded.Title).IsEqualTo("After-Rebuild Edit");
    }

    [Test]
    public async Task ApplyAsync_Throws_WhenSnapshotDriftsBetweenValidateAndApply()
    {
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var staleSnapshot = JsonSerializer.Serialize(
            ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null) with { Title = "Title Nobody Currently Sees" },
            JsonOptions);

        var change = new ReleaseFieldsUpdate(MakeProposed(title: "Proposed"));

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
        using var db = CreateDbContext();
        SeedRelease(db);
        var change = new ReleaseFieldsUpdate(MakeProposed(title: "x"));

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
        using var db = CreateDbContext();
        SeedRelease(db);
        var change = new ReleaseFieldsUpdate(MakeProposed(title: "x"));

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
        using var db = CreateDbContext();
        var release = SeedRelease(db);
        var current = ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null);
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
    public async Task ValidateAsync_NotConflict_WhenUnproposedFieldsDrift_DuringRebuild()
    {
        // Regression: a data rebuild may fill in previously-null fields (UPC, ASIN, etc.)
        // A title-only suggestion must not be blocked by drift in fields it never touched.
        using var db = CreateDbContext();
        var release = SeedRelease(db);

        // Snapshot at submission time: no UPC or ASIN.
        var snapshotAtSubmit = ReleaseFieldsUpdate.SnapshotFrom(release, MediaItemSlug, null)
            with { Upc = null, Asin = null };
        var snapshotJson = JsonSerializer.Serialize(snapshotAtSubmit, JsonOptions);

        // Rebuild adds UPC and ASIN.
        release.Upc = "999888777666";
        release.Asin = "B00NEW9999";
        await db.SaveChangesAsync();

        // Suggestion only proposes a Title change; Upc/Asin match the snapshot (null).
        var change = new ReleaseFieldsUpdate(snapshotAtSubmit with { Title = "Updated Title" });

        var result = await change.ValidateAsync(db, snapshotJson, CancellationToken.None);

        await Assert.That(result.IsConflict).IsFalse();
    }

    [Test]
    public async Task TargetEntityKey_FormatsAsParentSlugSlashReleaseSlug()
    {
        var details = MakeProposed();
        await Assert.That(details.TargetEntityKey).IsEqualTo($"{MediaItemSlug}/{ReleaseSlug}");

        var boxsetDetails = details with { MediaItemSlug = null, BoxsetSlug = "the-boxset" };
        await Assert.That(boxsetDetails.TargetEntityKey).IsEqualTo($"the-boxset/{ReleaseSlug}");
    }

    [Test]
    public async Task TypeKey_MatchesConstant()
    {
        var change = new ReleaseFieldsUpdate(MakeProposed());
        await Assert.That(change.TypeKey).IsEqualTo("release.fields.update");
        await Assert.That(change.TypeKey).IsEqualTo(ReleaseFieldsUpdate.Key);
    }
}
