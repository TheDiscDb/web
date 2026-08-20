namespace TheDiscDb.UnitTests.Services.Admin;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheDiscDb.Data.Import;
using TheDiscDb.Services.Admin;
using TheDiscDb.UnitTests.Data.Changes;
using TheDiscDb.Web.Data;

public class IntakeAdminServiceTests
{
    [Test]
    public async Task GetReleases_FiltersAndPagesByIntakeSearchFields()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var matching = CreateRelease("matching", "123456789012", "Collector Edition");
        matching.Discs.Add(CreateLink("A1", "disc-alpha", "Alpha Disc", 1));
        var other = CreateRelease("other", "123456789013", "Other Edition");
        other.Status = IntakeStatus.Rejected;
        other.Discs.Add(CreateLink("B1", "disc-beta", "Beta Disc", 1));
        database.AddRange(matching, other);
        await database.SaveChangesAsync();

        var service = CreateService(database).Service;
        var byDisc = await service.GetReleasesAsync(new IntakeReleaseSearch("Alpha Disc", null, 0, 25));
        var byStatus = await service.GetReleasesAsync(new IntakeReleaseSearch(null, IntakeStatus.Rejected, 0, 25));
        var paged = await service.GetReleasesAsync(new IntakeReleaseSearch(null, null, 0, 1));

        await Assert.That(byDisc.TotalCount).IsEqualTo(1);
        await Assert.That(byDisc.Items.Single().ReleaseTitle).IsEqualTo("Collector Edition");
        await Assert.That(byStatus.Items.Single().ReleaseTitle).IsEqualTo("Other Edition");
        await Assert.That(paged.TotalCount).IsEqualTo(2);
        await Assert.That(paged.Items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetRelease_IncludesIncompleteDiscCaptureReason()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("partial-release", "123456789012", "Partial");
        var link = CreateLink("A1", "disc-one", "Disc One", 1);
        link.IsPlaceholder = true;
        link.FailureReason = "MakeMKV log extraction failed: drive read error";
        release.Discs.Add(link);
        database.Add(release);
        await database.SaveChangesAsync();

        IntakeReleaseDetails? details = await CreateService(database).Service.GetReleaseAsync(release.Id);

        await Assert.That(details).IsNotNull();
        IntakeDiscLinkDetails disc = details!.Discs.Single();
        await Assert.That(disc.IsPlaceholder).IsTrue();
        await Assert.That(disc.FailureReason).IsEqualTo("MakeMKV log extraction failed: drive read error");
        await Assert.That(disc.ContentHash).IsEqualTo("A1");
    }

    [Test]
    public async Task UpdateRelease_ValidatesIdentityAndPreservesSourceReleaseId()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("immutable-source", "123456789012", "Original");
        database.Add(release);
        await database.SaveChangesAsync();

        var service = CreateService(database).Service;
        var invalid = await service.UpdateReleaseAsync(release.Id, new IntakeReleaseEditRequest
        {
            Status = IntakeStatus.ReadyForReview,
            ExternalProvider = "TMDB",
            ExternalId = "0",
            Upc = "123456789012",
        });
        var valid = await service.UpdateReleaseAsync(release.Id, new IntakeReleaseEditRequest
        {
            Status = IntakeStatus.Rejected,
            ExternalProvider = "TMDB",
            ExternalId = "000321",
            Upc = "1234-5678-9012",
            Asin = "B000000001",
            MediaItemSlug = "movie",
            ReleaseSlug = "release",
        });

        await Assert.That(invalid.Succeeded).IsFalse();
        await Assert.That(valid.Succeeded).IsTrue();
        await Assert.That(release.SourceReleaseId).IsEqualTo("immutable-source");
        await Assert.That(release.ExternalId).IsEqualTo("321");
        await Assert.That(release.Upc).IsEqualTo("123456789012");
        await Assert.That(release.Status).IsEqualTo(IntakeStatus.Rejected);
    }

    [Test]
    public async Task UpdateRelease_RejectsPromotedReleaseDemotion()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("promoted-release", "123456789012", "Promoted");
        release.Status = IntakeStatus.Promoted;
        database.Add(release);
        await database.SaveChangesAsync();

        var result = await CreateService(database).Service.UpdateReleaseAsync(release.Id, new IntakeReleaseEditRequest
        {
            Status = IntakeStatus.Pending,
            ExternalProvider = "NONE",
            ExternalId = "0",
            Upc = release.Upc,
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(release.Status).IsEqualTo(IntakeStatus.Promoted);
    }

    [Test]
    public async Task UpdateRelease_RejectsBothParentSlugs()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("two-parents", "123456789012", "Two parents");
        database.Add(release);
        await database.SaveChangesAsync();

        var result = await CreateService(database).Service.UpdateReleaseAsync(release.Id, new IntakeReleaseEditRequest
        {
            Status = IntakeStatus.Pending,
            ExternalProvider = "NONE",
            ExternalId = "0",
            Upc = release.Upc,
            MediaItemSlug = "movie",
            BoxsetSlug = "box-set",
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Errors.Single()).Contains("either a media item slug or a box set slug");
    }

    [Test]
    public async Task UpdateDiscs_RequiresConfirmationForSharedCanonicalFormat()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var shared = CreateDisc("shared-hash", "Blu-ray");
        var first = CreateRelease("first", "123456789012", "First");
        var second = CreateRelease("second", "123456789013", "Second");
        first.Discs.Add(new IntakeReleaseDisc
        {
            Disc = shared,
            Index = 1,
            Name = "First disc",
            Slug = "first-disc",
            SourceDiscId = "source-disc",
            GlobalDiscId = "GLOBAL-DISC-ID",
            AddedAt = DateTimeOffset.UtcNow,
        });
        second.Discs.Add(new IntakeReleaseDisc
        {
            Disc = shared,
            Index = 1,
            Name = "Second disc",
            Slug = "second-disc",
            AddedAt = DateTimeOffset.UtcNow,
        });
        database.AddRange(first, second);
        await database.SaveChangesAsync();

        var service = CreateService(database).Service;
        var request = new[]
        {
            new IntakeDiscLinkEditRequest
            {
                LinkId = first.Discs.Single().Id,
                Index = 1,
                Name = "Renamed disc",
                Slug = "renamed-disc",
                Format = "4K UHD",
            }
        };

        var unconfirmed = await service.UpdateDiscsAsync(first.Id, request, false);
        var confirmed = await service.UpdateDiscsAsync(first.Id, request, true);

        await Assert.That(unconfirmed.Succeeded).IsFalse();
        await Assert.That(confirmed.Succeeded).IsTrue();
        await Assert.That(shared.Format).IsEqualTo("4K");
        await Assert.That(first.Discs.Single().Name).IsEqualTo("Renamed disc");
        await Assert.That(first.Discs.Single().SourceDiscId).IsEqualTo("source-disc");
        await Assert.That(first.Discs.Single().GlobalDiscId).IsEqualTo("GLOBAL-DISC-ID");
        await Assert.That(shared.ContentHash).IsEqualTo("SHARED-HASH");
    }

    [Test]
    public async Task UpdateDiscs_RequiresConfirmationForSharedCanonicalFormat_WithSQLiteLinks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseSqlite(connection)
            .Options;
        await using var database = new SqlServerDataContext(options);
        await CreateSqliteIntakeSchemaAsync(database);

        var shared = CreateDisc("shared-sqlite-hash", "Blu-ray");
        var first = CreateRelease("sqlite-first", "123456789016", "First");
        var second = CreateRelease("sqlite-second", "123456789017", "Second");
        first.Discs.Add(new IntakeReleaseDisc
        {
            Disc = shared,
            Index = 1,
            Name = "First disc",
            Slug = "first-disc",
            SourceDiscId = "source-disc",
            GlobalDiscId = "GLOBAL-DISC-ID",
            AddedAt = DateTimeOffset.UtcNow,
        });
        second.Discs.Add(new IntakeReleaseDisc
        {
            Disc = shared,
            Index = 1,
            Name = "Second disc",
            Slug = "second-disc",
            AddedAt = DateTimeOffset.UtcNow,
        });
        database.AddRange(first, second);
        await database.SaveChangesAsync();

        var service = CreateService(database).Service;
        var request = new[]
        {
            new IntakeDiscLinkEditRequest
            {
                LinkId = first.Discs.Single().Id,
                Index = 1,
                Name = "Renamed disc",
                Slug = "renamed-disc",
                Format = "4K UHD",
            }
        };

        var unconfirmed = await service.UpdateDiscsAsync(first.Id, request, false);
        var confirmed = await service.UpdateDiscsAsync(first.Id, request, true);

        await Assert.That(shared.Releases.Count).IsEqualTo(2);
        await Assert.That(unconfirmed.Succeeded).IsFalse();
        await Assert.That(unconfirmed.Errors.Single()).Contains("Confirm this change before saving");
        await Assert.That(confirmed.Succeeded).IsTrue();
        await Assert.That(shared.Format).IsEqualTo("4K");
        await Assert.That(first.Discs.Single().Disc.Format).IsEqualTo("4K");
        await Assert.That(second.Discs.Single().Disc.Format).IsEqualTo("4K");
        await Assert.That(first.Discs.Single().Name).IsEqualTo("Renamed disc");
        await Assert.That(first.Discs.Single().SourceDiscId).IsEqualTo("source-disc");
        await Assert.That(first.Discs.Single().GlobalDiscId).IsEqualTo("GLOBAL-DISC-ID");
    }

    [Test]
    public async Task UpdateDiscs_RejectsDuplicateIndexes()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("duplicate-index", "123456789012", "Duplicate indexes");
        release.Discs.Add(CreateLink("first-hash", "first", "First", 1));
        release.Discs.Add(CreateLink("second-hash", "second", "Second", 2));
        database.Add(release);
        await database.SaveChangesAsync();

        var result = await CreateService(database).Service.UpdateDiscsAsync(
            release.Id,
            release.Discs.Select(link => new IntakeDiscLinkEditRequest
            {
                LinkId = link.Id,
                Index = 1,
                Name = link.Name!,
                Slug = link.Slug!,
                Format = link.Disc.Format,
            }).ToList(),
            false);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Errors.Single()).Contains("unique");
    }

    [Test]
    public async Task UpdateDiscs_RejectsUnsupportedFormatAndAllowsMissingNameAndSlug()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("disc-validation", "123456789012", "Disc validation");
        release.Discs.Add(CreateLink("disc-hash", "disc", "Disc", 1));
        database.Add(release);
        await database.SaveChangesAsync();
        var link = release.Discs.Single();
        var service = CreateService(database).Service;

        var unsupported = await service.UpdateDiscsAsync(
            release.Id,
            [new IntakeDiscLinkEditRequest
            {
                LinkId = link.Id,
                Index = 1,
                Name = null,
                Slug = null,
                Format = "LaserDisc",
            }],
            false);
        var nullable = await service.UpdateDiscsAsync(
            release.Id,
            [new IntakeDiscLinkEditRequest
            {
                LinkId = link.Id,
                Index = 1,
                Name = null,
                Slug = null,
                Format = "Blu-ray",
            }],
            false);

        await Assert.That(unsupported.Succeeded).IsFalse();
        await Assert.That(unsupported.Errors.Single()).Contains("not a supported disc format");
        await Assert.That(nullable.Succeeded).IsTrue();
        await Assert.That(link.Name).IsNull();
        await Assert.That(link.Slug).IsNull();
    }

    [Test]
    public async Task UpdateDiscs_SwapsIndexesAgainstRelationalUniqueIndex()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseSqlite(connection)
            .Options;
        await using var database = new SqlServerDataContext(options);
        await CreateSqliteIntakeSchemaAsync(database);

        var release = CreateRelease("sqlite-swap", "123456789012", "SQLite swap");
        release.Discs.Add(CreateLink("first-sqlite-hash", "first", "First", 1));
        release.Discs.Add(CreateLink("second-sqlite-hash", "second", "Second", 2));
        database.Add(release);
        await database.SaveChangesAsync();
        var links = release.Discs.OrderBy(link => link.Index).ToArray();

        var result = await CreateService(database).Service.UpdateDiscsAsync(
            release.Id,
            [
                new IntakeDiscLinkEditRequest
                {
                    LinkId = links[0].Id,
                    Index = 2,
                    Name = links[0].Name,
                    Slug = links[0].Slug,
                    Format = links[0].Disc.Format,
                },
                new IntakeDiscLinkEditRequest
                {
                    LinkId = links[1].Id,
                    Index = 1,
                    Name = links[1].Name,
                    Slug = links[1].Slug,
                    Format = links[1].Disc.Format,
                },
            ],
            false);

        var persisted = await database.IntakeReleaseDiscs
            .OrderBy(link => link.Index)
            .ToListAsync();
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(persisted[0].Id).IsEqualTo(links[1].Id);
        await Assert.That(persisted[1].Id).IsEqualTo(links[0].Id);
    }

    [Test]
    public async Task Images_ReplaceAndDeleteReleaseOwnedEvidence()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("image-release", "123456789012", "Images");
        var link = CreateLink("image-hash", "image-disc", "Image disc", 1);
        release.Discs.Add(link);
        release.FrontImageLocation = "intake/releases/old-front.jpg";
        link.Disc.Evidence.Add(new IntakeDiscEvidence
        {
            Source = release.Source,
            SourceEvidenceId = release.SourceReleaseId,
            EvidenceSetId = release.SourceReleaseId,
            Type = IntakeDiscEvidenceType.FrontImage,
            Location = release.FrontImageLocation,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        database.Add(release);
        await database.SaveChangesAsync();

        var (service, _, images) = CreateService(database);
        images.Add(release.FrontImageLocation, "old");
        await using var upload = new MemoryStream(Encoding.UTF8.GetBytes("new image"));
        var replacement = await service.ReplaceImageAsync(
            release.Id,
            IntakeImageSide.Front,
            upload,
            "cover.png",
            "image/png");
        string replacementLocation = release.FrontImageLocation!;
        var deletion = await service.DeleteImageAsync(release.Id, IntakeImageSide.Front);

        await Assert.That(replacement.Succeeded).IsTrue();
        await Assert.That(images.Deleted).Contains("intake/releases/old-front.jpg");
        await Assert.That(replacementLocation).Contains("front-");
        await Assert.That(replacementLocation).EndsWith(".png");
        await Assert.That(deletion.Succeeded).IsTrue();
        await Assert.That(release.FrontImageLocation).IsNull();
        await Assert.That(await database.IntakeDiscEvidence.CountAsync()).IsEqualTo(0);
        await Assert.That(images.Deleted).Contains(replacementLocation);
    }

    [Test]
    public async Task ReplaceImage_UpdatesReleaseOwnedFrontEvidenceRowsLeavesSharedRowsAndDeletesOldFrontPaths()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("front-image-release", "123456789014", "Front image");
        release.FrontImageLocation = "intake/releases/front-image-release/original-front.jpg";

        var firstLink = CreateLink("front-hash-a", "front-a", "Front A", 1);
        var secondLink = CreateLink("front-hash-b", "front-b", "Front B", 2);
        var recordedAt = DateTimeOffset.UtcNow.AddHours(-1);
        string oldFrontReleaseLocation = release.FrontImageLocation!;

        var ownedFrontA = CreateEvidence(
            release.Source,
            release.SourceReleaseId,
            release.SourceReleaseId,
            IntakeDiscEvidenceType.FrontImage,
            "intake/releases/front-image-release/front-a-old.jpg",
            "image/jpeg",
            "front-a-old",
            recordedAt);
        var sharedFront = CreateEvidence(
            release.Source,
            "other-release:front-shared",
            "other-release:front-shared",
            IntakeDiscEvidenceType.FrontImage,
            "intake/releases/shared-front.jpg",
            "image/jpeg",
            "shared-front-old",
            recordedAt);
        var ownedFrontB = CreateEvidence(
            release.Source,
            $"{release.SourceReleaseId}:front-b",
            $"{release.SourceReleaseId}:front-b",
            IntakeDiscEvidenceType.FrontImage,
            "intake/releases/front-image-release/front-b-old.png",
            "image/png",
            "front-b-old",
            recordedAt);

        firstLink.Disc.Evidence.Add(ownedFrontA);
        firstLink.Disc.Evidence.Add(sharedFront);
        secondLink.Disc.Evidence.Add(ownedFrontB);
        release.Discs.Add(firstLink);
        release.Discs.Add(secondLink);
        database.Add(release);
        await database.SaveChangesAsync();

        var (service, _, images) = CreateService(database);
        images.Add(oldFrontReleaseLocation, "legacy front");
        images.Add(ownedFrontA.Location, "front a");
        images.Add(ownedFrontB.Location, "front b");
        images.Add(sharedFront.Location, "shared front");

        var frontBytes = Encoding.UTF8.GetBytes("new front image");
        var frontHash = Convert.ToHexString(SHA256.HashData(frontBytes)).ToLowerInvariant();
        await using var upload = new MemoryStream(frontBytes);
        var result = await service.ReplaceImageAsync(
            release.Id,
            IntakeImageSide.Front,
            upload,
            "cover.png",
            "image/png");
        var newFrontLocation = release.FrontImageLocation!;

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(newFrontLocation).Contains("front-");
        await Assert.That(newFrontLocation).EndsWith(".png");
        await Assert.That(ownedFrontA.Location).IsEqualTo(newFrontLocation);
        await Assert.That(ownedFrontB.Location).IsEqualTo(newFrontLocation);
        await Assert.That(ownedFrontA.ContentType).IsEqualTo("image/png");
        await Assert.That(ownedFrontB.ContentType).IsEqualTo("image/png");
        await Assert.That(ownedFrontA.Sha256).IsEqualTo(frontHash);
        await Assert.That(ownedFrontB.Sha256).IsEqualTo(frontHash);
        await Assert.That(ownedFrontA.RecordedAt).IsGreaterThan(recordedAt);
        await Assert.That(ownedFrontB.RecordedAt).IsGreaterThan(recordedAt);
        await Assert.That(sharedFront.Location).IsEqualTo("intake/releases/shared-front.jpg");
        await Assert.That(sharedFront.ContentType).IsEqualTo("image/jpeg");
        await Assert.That(sharedFront.Sha256).IsEqualTo("shared-front-old");
        await Assert.That(images.Deleted).Contains(oldFrontReleaseLocation);
        await Assert.That(images.Deleted).Contains("intake/releases/front-image-release/front-a-old.jpg");
        await Assert.That(images.Deleted).Contains("intake/releases/front-image-release/front-b-old.png");
        await Assert.That(images.Deleted).DoesNotContain("intake/releases/shared-front.jpg");
        await Assert.That(images.Deleted.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ReplaceImage_UpdatesReleaseOwnedBackEvidenceRowsLeavesSharedRowsAndDeletesOldBackPaths()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("back-image-release", "123456789015", "Back image");
        release.FrontImageLocation = "intake/releases/back-image-release/original-front.jpg";

        var firstLink = CreateLink("back-hash-a", "back-a", "Back A", 1);
        var secondLink = CreateLink("back-hash-b", "back-b", "Back B", 2);
        var recordedAt = DateTimeOffset.UtcNow.AddHours(-1);
        string oldFrontLocation = release.FrontImageLocation!;

        var ownedBackA = CreateEvidence(
            release.Source,
            release.SourceReleaseId,
            release.SourceReleaseId,
            IntakeDiscEvidenceType.BackImage,
            "intake/releases/back-image-release/back-a-old.jpg",
            "image/jpeg",
            "back-a-old",
            recordedAt);
        var sharedBack = CreateEvidence(
            release.Source,
            "other-release:back-shared",
            "other-release:back-shared",
            IntakeDiscEvidenceType.BackImage,
            "intake/releases/shared-back.jpg",
            "image/jpeg",
            "shared-back-old",
            recordedAt);
        var ownedBackB = CreateEvidence(
            release.Source,
            $"{release.SourceReleaseId}:back-b",
            $"{release.SourceReleaseId}:back-b",
            IntakeDiscEvidenceType.BackImage,
            "intake/releases/back-image-release/back-b-old.webp",
            "image/webp",
            "back-b-old",
            recordedAt);

        firstLink.Disc.Evidence.Add(ownedBackA);
        firstLink.Disc.Evidence.Add(sharedBack);
        secondLink.Disc.Evidence.Add(ownedBackB);
        release.Discs.Add(firstLink);
        release.Discs.Add(secondLink);
        database.Add(release);
        await database.SaveChangesAsync();

        var (service, _, images) = CreateService(database);
        images.Add(oldFrontLocation, "legacy front");
        images.Add(ownedBackA.Location, "back a");
        images.Add(ownedBackB.Location, "back b");
        images.Add(sharedBack.Location, "shared back");

        var backBytes = Encoding.UTF8.GetBytes("new back image");
        var backHash = Convert.ToHexString(SHA256.HashData(backBytes)).ToLowerInvariant();
        await using var upload = new MemoryStream(backBytes);
        var result = await service.ReplaceImageAsync(
            release.Id,
            IntakeImageSide.Back,
            upload,
            "back.webp",
            "image/webp");
        var newBackLocation = ownedBackA.Location;

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(release.FrontImageLocation).IsEqualTo(oldFrontLocation);
        await Assert.That(newBackLocation).Contains("back-");
        await Assert.That(newBackLocation).EndsWith(".webp");
        await Assert.That(ownedBackA.Location).IsEqualTo(newBackLocation);
        await Assert.That(ownedBackB.Location).IsEqualTo(newBackLocation);
        await Assert.That(ownedBackA.ContentType).IsEqualTo("image/webp");
        await Assert.That(ownedBackB.ContentType).IsEqualTo("image/webp");
        await Assert.That(ownedBackA.Sha256).IsEqualTo(backHash);
        await Assert.That(ownedBackB.Sha256).IsEqualTo(backHash);
        await Assert.That(ownedBackA.RecordedAt).IsGreaterThan(recordedAt);
        await Assert.That(ownedBackB.RecordedAt).IsGreaterThan(recordedAt);
        await Assert.That(sharedBack.Location).IsEqualTo("intake/releases/shared-back.jpg");
        await Assert.That(sharedBack.ContentType).IsEqualTo("image/jpeg");
        await Assert.That(sharedBack.Sha256).IsEqualTo("shared-back-old");
        await Assert.That(images.Deleted).Contains("intake/releases/back-image-release/back-a-old.jpg");
        await Assert.That(images.Deleted).Contains("intake/releases/back-image-release/back-b-old.webp");
        await Assert.That(images.Deleted).DoesNotContain("intake/releases/shared-back.jpg");
        await Assert.That(images.Deleted).DoesNotContain(oldFrontLocation);
        await Assert.That(images.Deleted.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ReplaceImage_RejectsInvalidTypeAndOversizedContent()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("invalid-image", "123456789012", "Invalid image");
        release.Discs.Add(CreateLink("image-hash", "image", "Image", 1));
        database.Add(release);
        await database.SaveChangesAsync();
        var (service, _, images) = CreateService(database);

        await using var invalidContent = new MemoryStream([1]);
        var invalidType = await service.ReplaceImageAsync(
            release.Id,
            IntakeImageSide.Front,
            invalidContent,
            "cover.gif",
            "image/gif");
        await using var oversizedContent = new MemoryStream(new byte[(10 * 1024 * 1024) + 1]);
        var oversized = await service.ReplaceImageAsync(
            release.Id,
            IntakeImageSide.Front,
            oversizedContent,
            "cover.jpg",
            "image/jpeg");

        await Assert.That(invalidType.Succeeded).IsFalse();
        await Assert.That(oversized.Succeeded).IsFalse();
        await Assert.That(images.Saved).IsEmpty();
    }

    [Test]
    public async Task ReplaceImage_RejectsBackImageWithoutLinkedDisc()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("back-without-disc", "123456789012", "No disc");
        database.Add(release);
        await database.SaveChangesAsync();
        var (service, _, images) = CreateService(database);

        await using var upload = new MemoryStream(Encoding.UTF8.GetBytes("back"));
        var result = await service.ReplaceImageAsync(
            release.Id,
            IntakeImageSide.Back,
            upload,
            "back.jpg",
            "image/jpeg");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Errors.Single()).Contains("requires at least one linked disc");
        await Assert.That(images.Saved).IsEmpty();
    }

    [Test]
    public async Task DeleteRelease_BlocksPromotedReleases()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("promoted", "123456789012", "Promoted");
        release.Status = IntakeStatus.Promoted;
        database.Add(release);
        await database.SaveChangesAsync();

        var result = await CreateService(database).Service.DeleteReleaseAsync(release.Id);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(await database.IntakeReleases.AnyAsync(item => item.Id == release.Id)).IsTrue();
    }

    [Test]
    public async Task DeleteRelease_BlocksCompletedPromotionProvenance()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("completed-promotion", "123456789012", "Completed promotion");
        release.Promotions.Add(new IntakePromotion
        {
            Target = IntakePromotionTarget.Release,
            Status = IntakePromotionStatus.Completed,
            Source = IntakeSource.Engram,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        database.Add(release);
        await database.SaveChangesAsync();

        var result = await CreateService(database).Service.DeleteReleaseAsync(release.Id);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Errors.Single()).Contains("completed promotion provenance");
        await Assert.That(await database.IntakeReleases.AnyAsync(item => item.Id == release.Id)).IsTrue();
    }

    [Test]
    public async Task DeleteRelease_RemovesOrphansAndRetainsSharedDiscs()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var orphan = CreateDisc("orphan-hash", "Blu-ray");
        orphan.Evidence.Add(new IntakeDiscEvidence
        {
            Source = IntakeSource.Engram,
            SourceEvidenceId = "delete-me:disc-orphan",
            EvidenceSetId = "delete-me:disc-orphan",
            Type = IntakeDiscEvidenceType.DiscMetadata,
            Location = "intake/discs/orphan.json",
            RecordedAt = DateTimeOffset.UtcNow,
        });
        var shared = CreateDisc("shared-hash", "Blu-ray");
        shared.Evidence.Add(new IntakeDiscEvidence
        {
            Source = IntakeSource.Engram,
            SourceEvidenceId = "delete-me:disc-shared",
            EvidenceSetId = "delete-me:disc-shared",
            Type = IntakeDiscEvidenceType.FrontImage,
            Location = "intake/releases/delete-me/front.jpg",
            RecordedAt = DateTimeOffset.UtcNow,
        });
        shared.Evidence.Add(new IntakeDiscEvidence
        {
            Source = IntakeSource.Engram,
            SourceEvidenceId = "keep-me",
            EvidenceSetId = "keep-me",
            Type = IntakeDiscEvidenceType.ScanLog,
            Location = "intake/discs/shared.log",
            RecordedAt = DateTimeOffset.UtcNow,
        });

        var deleting = CreateRelease("delete-me", "123456789012", "Delete");
        deleting.FrontImageLocation = "intake/releases/delete-me/front.jpg";
        deleting.Discs.Add(new IntakeReleaseDisc { Disc = orphan, Index = 1, Name = "Orphan", Slug = "orphan", AddedAt = DateTimeOffset.UtcNow });
        deleting.Discs.Add(new IntakeReleaseDisc { Disc = shared, Index = 2, Name = "Shared", Slug = "shared", AddedAt = DateTimeOffset.UtcNow });
        deleting.Promotions.Add(new IntakePromotion
        {
            Target = IntakePromotionTarget.Release,
            Source = IntakeSource.Engram,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        orphan.Promotions.Add(new IntakePromotion
        {
            Target = IntakePromotionTarget.Disc,
            Source = IntakeSource.Engram,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var retaining = CreateRelease("keep-me", "123456789013", "Keep");
        retaining.Discs.Add(new IntakeReleaseDisc { Disc = shared, Index = 1, Name = "Shared", Slug = "shared", AddedAt = DateTimeOffset.UtcNow });
        database.AddRange(deleting, retaining);
        await database.SaveChangesAsync();

        var (service, assets, images) = CreateService(database);
        assets.Add("intake/discs/orphan.json", """{"release_manifest":"intake/manifests/delete-me.json"}""");
        var details = await service.GetReleaseAsync(deleting.Id);
        var result = await service.DeleteReleaseAsync(deleting.Id);

        await Assert.That(details!.ReleaseImages.Count).IsEqualTo(1);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.OrphanDiscCount).IsEqualTo(1);
        await Assert.That(await database.IntakeReleases.AnyAsync(item => item.Id == deleting.Id)).IsFalse();
        await Assert.That(await database.IntakeDiscs.AnyAsync(item => item.Id == orphan.Id)).IsFalse();
        await Assert.That(await database.IntakeDiscs.AnyAsync(item => item.Id == shared.Id)).IsTrue();
        await Assert.That(await database.IntakeDiscEvidence.AnyAsync(item => item.Location == "intake/discs/shared.log")).IsTrue();
        await Assert.That(images.Deleted).Contains("intake/releases/delete-me/front.jpg");
        await Assert.That(assets.Deleted).Contains("intake/discs/orphan.json");
        await Assert.That(assets.Deleted).Contains("intake/manifests/delete-me.json");
    }

    [Test]
    public async Task DeleteRelease_ReturnsWarningForMalformedMetadata()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("malformed-metadata", "123456789012", "Malformed metadata");
        var disc = CreateDisc("malformed-hash", "Blu-ray");
        disc.Evidence.Add(new IntakeDiscEvidence
        {
            Source = release.Source,
            SourceEvidenceId = $"{release.SourceReleaseId}:disc",
            EvidenceSetId = $"{release.SourceReleaseId}:disc",
            Type = IntakeDiscEvidenceType.DiscMetadata,
            Location = "intake/discs/malformed.json",
            RecordedAt = DateTimeOffset.UtcNow,
        });
        release.Discs.Add(new IntakeReleaseDisc
        {
            Disc = disc,
            Index = 1,
            Name = "Malformed",
            Slug = "malformed",
            AddedAt = DateTimeOffset.UtcNow,
        });
        database.Add(release);
        await database.SaveChangesAsync();
        var (service, assets, _) = CreateService(database);
        assets.Add("intake/discs/malformed.json", "{ not json");

        var result = await service.DeleteReleaseAsync(release.Id);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Warnings.Any(warning => warning.Contains("Could not parse metadata"))).IsTrue();
        await Assert.That(assets.Deleted).Contains("intake/discs/malformed.json");
    }

    [Test]
    public async Task DeleteRelease_ReturnsCleanupWarningAfterDatabaseCommit()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var release = CreateRelease("warning", "123456789012", "Warning");
        release.FrontImageLocation = "intake/releases/warning/front.jpg";
        database.Add(release);
        await database.SaveChangesAsync();

        var (service, _, images) = CreateService(database);
        images.FailDeletesFor.Add(release.FrontImageLocation);
        var result = await service.DeleteReleaseAsync(release.Id);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Warnings.Count).IsEqualTo(1);
        await Assert.That(await database.IntakeReleases.AnyAsync(item => item.Id == release.Id)).IsFalse();
    }

    private static IntakeRelease CreateRelease(string sourceReleaseId, string upc, string title) =>
        new()
        {
            Source = IntakeSource.Engram,
            SourceReleaseId = sourceReleaseId,
            Status = IntakeStatus.Pending,
            ReceivedAt = DateTimeOffset.UtcNow,
            ExternalProvider = "NONE",
            ExternalId = "0",
            Upc = upc,
            ReleaseTitle = title,
        };

    private static IntakeDiscEvidence CreateEvidence(
        IntakeSource source,
        string sourceEvidenceId,
        string evidenceSetId,
        IntakeDiscEvidenceType type,
        string location,
        string? contentType,
        string? sha256,
        DateTimeOffset recordedAt,
        string? globalDiscId = null) =>
        new()
        {
            Source = source,
            SourceEvidenceId = sourceEvidenceId,
            EvidenceSetId = evidenceSetId,
            GlobalDiscId = globalDiscId,
            Type = type,
            Location = location,
            ContentType = contentType,
            Sha256 = sha256,
            RecordedAt = recordedAt,
        };

    private static async Task CreateSqliteIntakeSchemaAsync(SqlServerDataContext database)
    {
        await database.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "IntakeReleases" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_IntakeReleases" PRIMARY KEY AUTOINCREMENT,
                "Source" TEXT NOT NULL,
                "SourceReleaseId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "ReceivedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NULL,
                "MediaItemSlug" TEXT NULL,
                "BoxsetSlug" TEXT NULL,
                "ReleaseSlug" TEXT NULL,
                "ExternalProvider" TEXT NOT NULL,
                "ExternalId" TEXT NOT NULL,
                "Upc" TEXT NOT NULL,
                "Asin" TEXT NULL,
                "ReleaseDate" TEXT NULL,
                "ReleaseTitle" TEXT NULL,
                "Locale" TEXT NULL,
                "RegionCode" TEXT NULL,
                "FrontImageLocation" TEXT NULL
            );
            CREATE TABLE "IntakeDiscs" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_IntakeDiscs" PRIMARY KEY AUTOINCREMENT,
                "Format" TEXT NOT NULL,
                "ContentHash" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "ReceivedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NULL
            );
            CREATE TABLE "IntakeReleaseDiscs" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_IntakeReleaseDiscs" PRIMARY KEY AUTOINCREMENT,
                "IntakeReleaseId" INTEGER NOT NULL,
                "IntakeDiscId" INTEGER NOT NULL,
                "SourceDiscId" TEXT NULL,
                "GlobalDiscId" TEXT NULL,
                "Index" INTEGER NULL,
                "Slug" TEXT NULL,
                "Name" TEXT NULL,
                "IsPlaceholder" INTEGER NOT NULL DEFAULT 0,
                "FailureReason" TEXT NULL,
                "AddedAt" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX "IX_IntakeReleaseDiscs_IntakeReleaseId_Index"
                ON "IntakeReleaseDiscs" ("IntakeReleaseId", "Index")
                WHERE "Index" IS NOT NULL;
            """);
    }

    private static IntakeReleaseDisc CreateLink(string hash, string slug, string name, int index) =>
        new()
        {
            Disc = CreateDisc(hash, "Blu-ray"),
            Index = index,
            Name = name,
            Slug = slug,
            AddedAt = DateTimeOffset.UtcNow,
        };

    private static IntakeDisc CreateDisc(string hash, string format) =>
        new()
        {
            ContentHash = hash,
            Format = format,
            Status = IntakeStatus.Pending,
            ReceivedAt = DateTimeOffset.UtcNow,
        };

    private static (IntakeAdminService Service, RecordingAssetStore Assets, RecordingAssetStore Images) CreateService(
        SqlServerDataContext database)
    {
        var assets = new RecordingAssetStore();
        var images = new RecordingAssetStore();
        return (new IntakeAdminService(database, assets, images, NullLogger<IntakeAdminService>.Instance), assets, images);
    }

    private sealed class RecordingAssetStore : IStaticAssetStore
    {
        private readonly Dictionary<string, BinaryData> files = new(StringComparer.Ordinal);

        public string ContainerName { get; set; } = "test";
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
        public HashSet<string> FailDeletesFor { get; } = new(StringComparer.Ordinal);

        public void Add(string location, string content) =>
            files[location] = BinaryData.FromString(content);

        public Task<string> Save(string filePath, string remotePath, string contentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<string> Save(Stream stream, string remotePath, string contentType, CancellationToken cancellationToken = default)
        {
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, cancellationToken);
            files[remotePath] = BinaryData.FromBytes(output.ToArray());
            Saved.Add(remotePath);
            return remotePath;
        }

        public Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(files.ContainsKey(remotePath));

        public Task<BinaryData> Download(string remotePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(files[remotePath]);

        public Task Delete(string remotePath, CancellationToken cancellationToken = default)
        {
            if (FailDeletesFor.Contains(remotePath))
            {
                throw new InvalidOperationException("Configured cleanup failure.");
            }

            Deleted.Add(remotePath);
            files.Remove(remotePath);
            return Task.CompletedTask;
        }
    }
}
