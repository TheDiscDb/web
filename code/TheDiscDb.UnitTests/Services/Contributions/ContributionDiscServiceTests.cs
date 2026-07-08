namespace TheDiscDb.UnitTests.Services.Contributions;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sqids;
using TheDiscDb.Data.Import;
using TheDiscDb.Services.Contributions;
using TheDiscDb.UnitTests.Data.Changes;
using TheDiscDb.Web.Data;

public class ContributionDiscServiceTests
{
    private const string ContentHash = "AAAA1111BBBB2222CCCC3333DDDD4444";
    private const string DiscId = "A734E4BEE726B8943F2E8817E3956EFC5F786C8B";

    private static string ValidLogs()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-disc-log.txt"));

    private sealed class RecordingAssetStore : IStaticAssetStore
    {
        public string ContainerName { get; set; } = "contributions";
        public string? SavedPath { get; private set; }
        public string? SavedContent { get; private set; }
        public int SaveCount { get; private set; }

        public async Task<string> Save(Stream stream, string remotePath, string contentType, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SavedPath = remotePath;
            using var reader = new StreamReader(stream);
            SavedContent = await reader.ReadToEndAsync(cancellationToken);
            return remotePath;
        }

        public Task<string> Save(string filePath, string remotePath, string contentType, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<BinaryData> Download(string remotePath, CancellationToken cancellationToken = default) => Task.FromResult(BinaryData.Empty);
        public Task Delete(string remotePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static (ContributionDiscService Service, RecordingAssetStore Store, SqidsEncoder<int> Encoder) CreateService(SqlServerDataContext db)
    {
        var store = new RecordingAssetStore();
        var encoder = new SqidsEncoder<int>();
        return (new ContributionDiscService(db, store, encoder), store, encoder);
    }

    [Test]
    public async Task CreatePendingDiscContribution_NoLogs_CreatesPendingDisc_NoUpload()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var (service, store, _) = CreateService(db);

        var result = await service.CreatePendingDiscContributionAsync("user-1", new ContributionDiscRequest
        {
            ContentHash = ContentHash,
            GlobalDiscId = DiscId,
            Format = "Blu-ray",
            Name = "Disc 1",
            Slug = "disc-1",
        });

        await Assert.That(result.LogsUploaded).IsFalse();
        await Assert.That(store.SaveCount).IsEqualTo(0);

        var disc = await db.UserContributionDiscs.FindAsync(result.DiscId);
        await Assert.That(disc!.GlobalDiscId).IsEqualTo(DiscId);
        await Assert.That(disc.ContentHash).IsEqualTo(ContentHash);

        var contribution = await db.UserContributions.FindAsync(result.ContributionId);
        await Assert.That(contribution!.Status).IsEqualTo(UserContributionStatus.Pending);
        await Assert.That(contribution.UserId).IsEqualTo("user-1");
    }

    [Test]
    public async Task CreatePendingDiscContribution_ValidLogs_UploadsToCanonicalPath()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var (service, store, encoder) = CreateService(db);

        var result = await service.CreatePendingDiscContributionAsync("user-1", new ContributionDiscRequest
        {
            ContentHash = ContentHash,
            GlobalDiscId = DiscId,
            Format = "Blu-ray",
            Name = "Disc 1",
            Slug = "disc-1",
            DiscLogs = ValidLogs(),
        });

        await Assert.That(result.LogsUploaded).IsTrue();
        await Assert.That(store.SaveCount).IsEqualTo(1);
        await Assert.That(store.SavedPath).IsEqualTo($"{result.EncodedContributionId}/{result.EncodedDiscId}-logs.txt");
        await Assert.That(result.EncodedContributionId).IsEqualTo(encoder.Encode(result.ContributionId));

        var disc = await db.UserContributionDiscs.FindAsync(result.DiscId);
        await Assert.That(disc!.LogsUploaded).IsTrue();
        await Assert.That(disc.LogUploadError).IsNull();
    }

    [Test]
    public async Task CreatePendingDiscContribution_InvalidLogs_FlagsErrorWithoutUpload()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var (service, store, _) = CreateService(db);

        var result = await service.CreatePendingDiscContributionAsync("user-1", new ContributionDiscRequest
        {
            ContentHash = ContentHash,
            Format = "Blu-ray",
            Name = "Disc 1",
            Slug = "disc-1",
            DiscLogs = "this is not a makemkv log\r\nneither is this",
        });

        await Assert.That(result.LogsUploaded).IsFalse();
        await Assert.That(result.LogUploadError).IsNotNull();
        await Assert.That(store.SaveCount).IsEqualTo(0);

        var disc = await db.UserContributionDiscs.FindAsync(result.DiscId);
        await Assert.That(disc!.LogUploadError).IsNotNull();
    }
}
