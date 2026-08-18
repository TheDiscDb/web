namespace TheDiscDb.UnitTests.Services.Contributions;

using System.Text;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb.Data.Import;
using TheDiscDb.InputModels;
using TheDiscDb.Services.Contributions;
using TheDiscDb.UnitTests.Data.Changes;
using TheDiscDb.Web.Data;

public class IntakeMatchingServiceTests
{
    private const string ContentHash = "AAAA1111BBBB2222";
    private const string SubmittedDiscId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string EvidenceDiscId = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Test]
    public async Task FindReleaseMatch_NormalizesTmdbAndUpc_RequiresExactPair()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        database.IntakeReleases.AddRange(
            new IntakeRelease
            {
                Source = IntakeSource.Engram,
                SourceReleaseId = "matching",
                Status = IntakeStatus.ReadyForReview,
                ReceivedAt = DateTimeOffset.UtcNow,
                ExternalProvider = " tmdb ",
                ExternalId = "000123",
                Upc = "1-23456-78901-2",
                Asin = "B000000001",
                ReleaseTitle = "Collector's Edition",
                FrontImageLocation = "intake/releases/matching/front.jpg",
            },
            new IntakeRelease
            {
                Source = IntakeSource.Engram,
                SourceReleaseId = "different-upc",
                Status = IntakeStatus.ReadyForReview,
                ReceivedAt = DateTimeOffset.UtcNow,
                ExternalProvider = "TMDB",
                ExternalId = "123",
                Upc = "123456789013",
                ReleaseTitle = "Wrong release",
            });
        await database.SaveChangesAsync();

        var service = CreateService(database).Service;
        var match = await service.FindReleaseMatchAsync("TmDb", "123", "1234 5678 9012");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.ReleaseTitle).IsEqualTo("Collector's Edition");
        await Assert.That(match.Asin).IsEqualTo("B000000001");
        await Assert.That(match.FrontImageUrl).IsEqualTo("/images/intake/releases/matching/front.jpg");

        var noMatch = await service.FindReleaseMatchAsync("TMDB", "123", "123456789014");
        await Assert.That(noMatch).IsNull();
    }

    [Test]
    public async Task PromoteDisc_UsesLatestFallbackEvidenceSet_RecordsMismatchProvenance()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, encoder) = CreateService(database);
        store.Add("intake/discs/older.log", "older higher-quality intake log");
        store.Add("intake/discs/best.log", "best intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash.ToLowerInvariant(),
                "4K UHD",
                "User Disc Name",
                "user-disc",
                SubmittedDiscId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Promoted).IsTrue();
        await Assert.That(result.LogsCopied).IsTrue();
        await Assert.That(result.GlobalDiscIdMismatch).IsTrue();
        await Assert.That(result.EvidenceGlobalDiscId).IsEqualTo(EvidenceDiscId);
        await Assert.That(result.Disc.Name).IsEqualTo("User Disc Name");
        await Assert.That(result.Disc.Slug).IsEqualTo("user-disc");
        await Assert.That(result.Disc.GlobalDiscId).IsEqualTo(SubmittedDiscId);
        await Assert.That(result.Disc.LogsUploaded).IsTrue();
        await Assert.That(store.SavedPath).IsEqualTo(
            $"{encoder.Encode(contribution.Id)}/{encoder.Encode(result.Disc.Id)}-logs.txt");
        await Assert.That(store.SavedContent).IsEqualTo("best intake log");

        var promotion = await database.IntakePromotions.SingleAsync();
        await Assert.That(promotion.Status).IsEqualTo(IntakePromotionStatus.Completed);
        await Assert.That(promotion.DiscGlobalId).IsEqualTo(SubmittedDiscId);
        await Assert.That(promotion.DiscContentHash).IsEqualTo(ContentHash);
        await Assert.That(promotion.RequestedByUserId).IsEqualTo("owner");
        await Assert.That(promotion.EvidenceSetId).IsEqualTo("best-capture");
    }

    [Test]
    public async Task PromoteDisc_PrefersOlderExactGlobalDiscIdEvidenceSet()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        var intakeDisc = SeedIntakeDisc(database);
        intakeDisc.Evidence.Add(new IntakeDiscEvidence
        {
            Source = IntakeSource.Engram,
            SourceEvidenceId = "exact-metadata",
            EvidenceSetId = "exact-capture",
            GlobalDiscId = SubmittedDiscId.ToLowerInvariant(),
            Type = IntakeDiscEvidenceType.DiscMetadata,
            Location = "intake/discs/exact.json",
            ContentType = "application/json",
            RecordedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        intakeDisc.Evidence.Add(new IntakeDiscEvidence
        {
            Source = IntakeSource.Engram,
            SourceEvidenceId = "exact-log",
            EvidenceSetId = "exact-capture",
            Type = IntakeDiscEvidenceType.ScanLog,
            Location = "intake/discs/exact.log",
            ContentType = ContentTypes.TextContentType,
            RecordedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/exact.log", "exact Disc ID intake log");
        store.Add("intake/discs/best.log", "newer different Disc ID intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash,
                DiscFormatConstants.FourK,
                "Disc 1",
                "disc-1",
                SubmittedDiscId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GlobalDiscIdMismatch).IsFalse();
        await Assert.That(result.EvidenceGlobalDiscId).IsEqualTo(SubmittedDiscId);
        await Assert.That(store.SavedContent).IsEqualTo("exact Disc ID intake log");

        var promotion = await database.IntakePromotions.SingleAsync();
        await Assert.That(promotion.EvidenceSetId).IsEqualTo("exact-capture");
    }

    [Test]
    public async Task PromoteDisc_PreferredEvidenceMissing_FallsBackToOlderReadableLog()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/older.log", "older readable intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash,
                DiscFormatConstants.FourK,
                "Disc 1",
                "disc-1",
                SubmittedDiscId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.LogsCopied).IsTrue();
        await Assert.That(result.GlobalDiscIdMismatch).IsTrue();
        await Assert.That(result.EvidenceGlobalDiscId).IsEqualTo(new string('C', 40));
        await Assert.That(store.SavedContent).IsEqualTo("older readable intake log");

        var promotion = await database.IntakePromotions.SingleAsync();
        await Assert.That(promotion.EvidenceSetId).IsEqualTo("older-capture");
    }

    [Test]
    public async Task PromoteDisc_RepeatedWithinContribution_IsIdempotent()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/best.log", "best intake log");
        var request = new IntakeDiscPromotionRequest(
            ContentHash,
            DiscFormatConstants.FourK,
            "Disc 1",
            "disc-1",
            SubmittedDiscId);

        var first = await service.PromoteDiscAsync(contribution.Id, "owner", request);
        var second = await service.PromoteDiscAsync(contribution.Id, "owner", request);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(second!.Promoted).IsFalse();
        await Assert.That(second.Disc.Id).IsEqualTo(first!.Disc.Id);
        await Assert.That(store.SaveCount).IsEqualTo(2);
        await Assert.That(await database.UserContributionDiscs.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.IntakePromotions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task PromoteDisc_AfterPromotedDiscDeletion_RestoresDiscAndLog()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, encoder) = CreateService(database);
        store.Add("intake/discs/best.log", "best intake log");
        var request = new IntakeDiscPromotionRequest(
            ContentHash,
            DiscFormatConstants.FourK,
            "Disc 1",
            "disc-1",
            SubmittedDiscId);

        var first = await service.PromoteDiscAsync(contribution.Id, "owner", request);
        string firstLogPath =
            $"{encoder.Encode(contribution.Id)}/{encoder.Encode(first!.Disc.Id)}-logs.txt";
        database.UserContributionDiscs.Remove(first.Disc);
        await database.SaveChangesAsync();
        await store.Delete(firstLogPath);

        var second = await service.PromoteDiscAsync(contribution.Id, "owner", request);

        await Assert.That(second).IsNotNull();
        await Assert.That(second!.Promoted).IsFalse();
        await Assert.That(second.LogsCopied).IsTrue();
        await Assert.That(second.Disc.Id).IsNotEqualTo(first.Disc.Id);
        await Assert.That(store.SaveCount).IsEqualTo(4);
        await Assert.That(await database.UserContributionDiscs.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.IntakePromotions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task PromoteDisc_ExistingContributorLog_IsPreserved()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        var contributionDisc = new UserContributionDisc
        {
            Format = DiscFormatConstants.FourK,
            ContentHash = ContentHash,
            Name = "Contributor Disc",
            Slug = "contributor-disc",
            LogsUploaded = true,
        };
        contribution.Discs.Add(contributionDisc);
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, encoder) = CreateService(database);
        var contributionLogPath =
            $"{encoder.Encode(contribution.Id)}/{encoder.Encode(contributionDisc.Id)}-logs.txt";
        store.Add(contributionLogPath, "contributor log");
        store.Add("intake/discs/best.log", "intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash,
                DiscFormatConstants.FourK,
                "Disc 1",
                "disc-1",
                SubmittedDiscId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.LogsCopied).IsTrue();
        await Assert.That(store.SaveCount).IsEqualTo(0);
        await Assert.That((await store.Download(contributionLogPath)).ToString())
            .IsEqualTo("contributor log");
    }

    [Test]
    public async Task PromoteDisc_NotOwned_ReturnsNullWithoutWriting()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/best.log", "best intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "different-user",
            new IntakeDiscPromotionRequest(
                ContentHash,
                DiscFormatConstants.FourK,
                "Disc 1",
                "disc-1",
                SubmittedDiscId));

        await Assert.That(result).IsNull();
        await Assert.That(store.SaveCount).IsEqualTo(0);
        await Assert.That(await database.UserContributionDiscs.CountAsync()).IsEqualTo(0);
        await Assert.That(await database.IntakePromotions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PromoteDisc_NonEditableContribution_ReturnsNullWithoutWriting()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        contribution.Status = UserContributionStatus.ReadyForReview;
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/best.log", "best intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash,
                DiscFormatConstants.FourK,
                "Disc 1",
                "disc-1",
                SubmittedDiscId));

        await Assert.That(result).IsNull();
        await Assert.That(store.SaveCount).IsEqualTo(0);
        await Assert.That(await database.UserContributionDiscs.CountAsync()).IsEqualTo(0);
        await Assert.That(await database.IntakePromotions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PromoteDisc_SameHashDifferentFormat_DoesNotReuseContributionDisc()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        contribution.Discs.Add(new UserContributionDisc
        {
            Format = DiscFormatConstants.BluRay,
            ContentHash = ContentHash,
            Name = "Blu-ray",
            Slug = "blu-ray",
        });
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/best.log", "best intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash,
                "UHD",
                "4K Disc",
                "4k-disc",
                SubmittedDiscId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Disc.Format).IsEqualTo(DiscFormatConstants.FourK);
        await Assert.That(await database.UserContributionDiscs.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task PromoteDisc_BoxsetContribution_AddsDiscMember()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        contribution.Boxset = new UserContributionBoxset
        {
            UserId = "owner",
            Created = DateTimeOffset.UtcNow,
            Slug = "boxset",
        };
        SeedIntakeDisc(database);
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/best.log", "best intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash,
                DiscFormatConstants.FourK,
                "Disc 1",
                "disc-1",
                SubmittedDiscId));

        await Assert.That(result).IsNotNull();
        var member = await database.UserContributionBoxsetMembers.SingleAsync();
        await Assert.That(member.Disc!.Id).IsEqualTo(result!.Disc.Id);
        await Assert.That(member.SortOrder).IsEqualTo(0);
    }

    [Test]
    public async Task PromoteDisc_MainDatabaseMatch_DoesNotPromoteIntake()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        SeedIntakeDisc(database);
        database.Discs.Add(new Disc
        {
            ContentHash = ContentHash,
            Format = DiscFormatConstants.FourK,
        });
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/best.log", "best intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash,
                DiscFormatConstants.FourK,
                "Disc 1",
                "disc-1",
                SubmittedDiscId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.MainDatabaseMatch).IsTrue();
        await Assert.That(store.SaveCount).IsEqualTo(0);
        await Assert.That(await database.UserContributionDiscs.CountAsync()).IsEqualTo(0);
        await Assert.That(await database.IntakePromotions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PromoteDisc_MainDatabaseSameHashDifferentFormat_AllowsPromotion()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        SeedIntakeDisc(database);
        database.Discs.Add(new Disc
        {
            ContentHash = ContentHash,
            Format = DiscFormatConstants.BluRay,
        });
        await database.SaveChangesAsync();

        var (service, store, _) = CreateService(database);
        store.Add("intake/discs/best.log", "best intake log");

        var result = await service.PromoteDiscAsync(
            contribution.Id,
            "owner",
            new IntakeDiscPromotionRequest(
                ContentHash,
                "4K UHD",
                "Disc 1",
                "disc-1",
                SubmittedDiscId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Promoted).IsTrue();
        await Assert.That(result.MainDatabaseMatch).IsFalse();
    }

    [Test]
    public async Task RecordReleasePromotion_IsOwnedAndIdempotent()
    {
        using var database = ChangeTestSeed.CreateDbContext();
        var contribution = SeedContribution(database, "owner");
        database.IntakeReleases.Add(new IntakeRelease
        {
            Source = IntakeSource.Engram,
            SourceReleaseId = "release",
            Status = IntakeStatus.ReadyForReview,
            ReceivedAt = DateTimeOffset.UtcNow,
            ExternalProvider = "TMDB",
            ExternalId = "123",
            Upc = "123456789012",
        });
        await database.SaveChangesAsync();

        var service = CreateService(database).Service;
        var denied = await service.RecordReleasePromotionAsync(contribution.Id, "different-user");
        var first = await service.RecordReleasePromotionAsync(contribution.Id, "owner");
        var second = await service.RecordReleasePromotionAsync(contribution.Id, "owner");
        contribution.Status = UserContributionStatus.ReadyForReview;
        await database.SaveChangesAsync();
        var nonEditable = await service.RecordReleasePromotionAsync(contribution.Id, "owner");

        await Assert.That(denied).IsFalse();
        await Assert.That(first).IsTrue();
        await Assert.That(second).IsTrue();
        await Assert.That(nonEditable).IsFalse();
        await Assert.That(await database.IntakePromotions.CountAsync()).IsEqualTo(1);
    }

    private static UserContribution SeedContribution(SqlServerDataContext database, string userId)
    {
        var contribution = new UserContribution
        {
            UserId = userId,
            Created = DateTimeOffset.UtcNow,
            ExternalProvider = "TMDB",
            ExternalId = "123",
            Upc = "123456789012",
            ReleaseSlug = "release",
        };
        database.UserContributions.Add(contribution);
        return contribution;
    }

    private static IntakeDisc SeedIntakeDisc(SqlServerDataContext database)
    {
        var release = new IntakeRelease
        {
            Source = IntakeSource.Engram,
            SourceReleaseId = "release",
            Status = IntakeStatus.ReadyForReview,
            ReceivedAt = DateTimeOffset.UtcNow,
            ExternalProvider = "TMDB",
            ExternalId = "123",
            Upc = "123456789012",
            ReleaseSlug = "release",
        };
        var disc = new IntakeDisc
        {
            ContentHash = ContentHash.ToLowerInvariant(),
            Format = "uhd",
            Status = IntakeStatus.ReadyForReview,
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        disc.Evidence.Add(new IntakeDiscEvidence
        {
            Source = IntakeSource.Api,
            SourceEvidenceId = "older",
            EvidenceSetId = "older-capture",
            GlobalDiscId = new string('C', 40),
            Type = IntakeDiscEvidenceType.ScanLog,
            Location = "intake/discs/older.log",
            ContentType = ContentTypes.TextContentType,
            Sha256 = new string('C', 64),
            RecordedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        disc.Evidence.Add(new IntakeDiscEvidence
        {
            Source = IntakeSource.Engram,
            SourceEvidenceId = "best-metadata",
            EvidenceSetId = "best-capture",
            GlobalDiscId = EvidenceDiscId.ToLowerInvariant(),
            Type = IntakeDiscEvidenceType.DiscMetadata,
            Location = "intake/discs/best.json",
            ContentType = "application/json",
            RecordedAt = DateTimeOffset.UtcNow,
        });
        disc.Evidence.Add(new IntakeDiscEvidence
        {
            Source = IntakeSource.Engram,
            SourceEvidenceId = "best",
            EvidenceSetId = "best-capture",
            Type = IntakeDiscEvidenceType.ScanLog,
            Location = "intake/discs/best.log",
            ContentType = "application/octet-stream",
            RecordedAt = DateTimeOffset.UtcNow,
        });
        release.Discs.Add(new IntakeReleaseDisc
        {
            Disc = disc,
            AddedAt = DateTimeOffset.UtcNow,
            GlobalDiscId = EvidenceDiscId,
            Name = "Intake Disc Name",
            Slug = "intake-disc",
        });
        database.IntakeReleases.Add(release);
        return disc;
    }

    private static (IntakeMatchingService Service, RecordingAssetStore Store, SqidsEncoder<int> Encoder)
        CreateService(SqlServerDataContext database)
    {
        var store = new RecordingAssetStore();
        var encoder = new SqidsEncoder<int>();
        return (new IntakeMatchingService(database, store, encoder), store, encoder);
    }

    private sealed class RecordingAssetStore : IStaticAssetStore
    {
        private readonly Dictionary<string, BinaryData> files = new(StringComparer.Ordinal);

        public string ContainerName { get; set; } = "contributions";
        public int SaveCount { get; private set; }
        public string? SavedPath { get; private set; }
        public string? SavedContent { get; private set; }

        public void Add(string path, string content) =>
            this.files[path] = BinaryData.FromString(content);

        public async Task<string> Save(
            Stream stream,
            string remotePath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            this.SaveCount++;
            this.SavedPath = remotePath;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            this.SavedContent = await reader.ReadToEndAsync(cancellationToken);
            this.files[remotePath] = BinaryData.FromString(this.SavedContent);
            return remotePath;
        }

        public Task<string> Save(
            string filePath,
            string remotePath,
            string contentType,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.files.ContainsKey(remotePath));

        public Task<BinaryData> Download(string remotePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.files[remotePath]);

        public Task Delete(string remotePath, CancellationToken cancellationToken = default)
        {
            this.files.Remove(remotePath);
            return Task.CompletedTask;
        }
    }
}
