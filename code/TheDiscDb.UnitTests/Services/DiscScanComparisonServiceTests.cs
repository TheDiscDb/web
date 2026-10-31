using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;
using ConditionalAssetStore = TheDiscDb.UnitTests.Services.DiscScanComparisonStoreTests.ConditionalAssetStore;

namespace TheDiscDb.UnitTests.Services;

public class DiscScanComparisonServiceTests
{
    [Test]
    public async Task RecompareDiscAsync_PersistsOriginalByteProvenanceAndSequentialHistories()
    {
        var factory = new InMemoryContextFactory();
        await using var db = factory.CreateDbContext();
        var disc = await SeedDisc(db, "first");
        var assets = new ConditionalAssetStore();
        var encoder = new IdEncoder(new SqidsEncoder<int>());
        var store = new DiscScanComparisonStore(assets);
        var service = new DiscScanComparisonService(factory, assets, encoder, store);
        var input = await SeedInputs(assets, encoder, disc);

        var first = await service.RecompareDiscAsync(disc.Id);
        disc.ManifestUserAgent = "second";
        await db.SaveChangesAsync();
        var second = await service.RecompareDiscAsync(disc.Id);

        var history = (await store.ReadAsync(encoder.Encode(disc.UserContribution.Id), encoder.Encode(disc.Id)))!;
        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(history.Comparisons.Count).IsEqualTo(2);
        await Assert.That(history.Comparisons[0].UserAgent).IsEqualTo("second");
        await Assert.That(history.Comparisons[1].UserAgent).IsEqualTo("first");
        string version = typeof(DiscLogManifestComparer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(DiscLogManifestComparer).Assembly.GetName().Version?.ToString() ?? "unknown";
        foreach (var record in history.Comparisons)
        {
            await Assert.That(record.Status == DiscLogManifestComparisonStatus.Error).IsFalse();
            await Assert.That(record.ManifestSha256).IsEqualTo(Convert.ToHexString(SHA256.HashData(input.Manifest)));
            await Assert.That(record.ReferenceSha256).IsEqualTo(Convert.ToHexString(SHA256.HashData(input.Log)));
            await Assert.That(record.ComparerVersion).IsEqualTo(version);
        }
        await Assert.That(assets.WriteAttempts).IsEqualTo(2);
        await Assert.That(assets.SaveCount).IsEqualTo(2); // Only the two original input assets use Save.
    }

    [Test]
    public async Task RecompareAllDiscsAsync_CountsSkippedMalformedAndStorageFailuresAndContinues()
    {
        var factory = new InMemoryContextFactory();
        await using var db = factory.CreateDbContext();
        var failed = await SeedDisc(db, "storage-failure");
        var missingManifest = await SeedDisc(db, "missing-manifest");
        var malformed = await SeedDisc(db, "malformed");
        var valid = await SeedDisc(db, "valid-after-failure");
        var missingLog = await SeedDisc(db, "missing-log");
        var encoder = new IdEncoder(new SqidsEncoder<int>());
        string failedPath = ContributionDiscAssets.ComparisonPath(encoder.Encode(failed.UserContribution.Id), encoder.Encode(failed.Id));
        var assets = new ConditionalAssetStore
        {
            WriteFailureForPath = path => path == failedPath ? new IOException("Forced persistence failure") : null
        };
        var store = new DiscScanComparisonStore(assets);
        var service = new DiscScanComparisonService(factory, assets, encoder, store);
        await SeedInputs(assets, encoder, failed);
        await SeedInputs(assets, encoder, missingManifest, includeManifest: false);
        await SeedInputs(assets, encoder, malformed, malformedManifest: true);
        await SeedInputs(assets, encoder, valid);
        await SeedInputs(assets, encoder, missingLog, includeLog: false);

        var result = await service.RecompareAllDiscsAsync();

        await Assert.That(result.Attempted).IsEqualTo(5);
        await Assert.That(result.Computed).IsEqualTo(3);
        await Assert.That(result.Persisted).IsEqualTo(2);
        await Assert.That(result.Skipped).IsEqualTo(2);
        await Assert.That(result.ErrorRecords).IsEqualTo(1);
        await Assert.That(result.Failures.Count).IsEqualTo(1);
        await Assert.That(result.Failures[0].DiscId).IsEqualTo(failed.Id);
        await Assert.That(result.Failures[0].Error).Contains("Forced persistence failure");
        await Assert.That(assets.WriteAttempts).IsEqualTo(3);
        await Assert.That(await ReadHistory(store, encoder, failed)).IsNull();
        await Assert.That(await ReadHistory(store, encoder, missingManifest)).IsNull();
        await Assert.That(await ReadHistory(store, encoder, missingLog)).IsNull();
        var malformedHistory = (await ReadHistory(store, encoder, malformed))!;
        await Assert.That(malformedHistory.Comparisons.Count).IsEqualTo(1);
        await Assert.That(malformedHistory.Comparisons[0].Status).IsEqualTo(DiscLogManifestComparisonStatus.Error);
        await Assert.That(malformedHistory.Comparisons[0].ManifestSha256).IsEqualTo(Convert.ToHexString(SHA256.HashData("not json"u8)));
        var validHistory = (await ReadHistory(store, encoder, valid))!;
        await Assert.That(validHistory.Comparisons.Count).IsEqualTo(1);
        await Assert.That(validHistory.Comparisons[0].Status == DiscLogManifestComparisonStatus.Error).IsFalse();
    }

    [Test]
    public async Task RecompareDiscAsync_StorageFailurePropagatesWithoutTryingToSaveAnErrorRecord()
    {
        var factory = new InMemoryContextFactory();
        await using var db = factory.CreateDbContext();
        var disc = await SeedDisc(db, "failure");
        var failure = new IOException("Forced persistence failure");
        var assets = new ConditionalAssetStore { WriteFailure = failure };
        var encoder = new IdEncoder(new SqidsEncoder<int>());
        var store = new DiscScanComparisonStore(assets);
        var service = new DiscScanComparisonService(factory, assets, encoder, store);
        await SeedInputs(assets, encoder, disc);

        var exception = await Assert.ThrowsAsync<IOException>(() => service.RecompareDiscAsync(disc.Id));

        await Assert.That(ReferenceEquals(exception, failure)).IsTrue();
        await Assert.That(assets.WriteAttempts).IsEqualTo(1);
        await Assert.That(await ReadHistory(store, encoder, disc)).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RecompareAsync_RequestedCancellationDuringInputReadIsNotSavedAsError(bool batch)
    {
        var factory = new InMemoryContextFactory();
        await using var db = factory.CreateDbContext();
        var disc = await SeedDisc(db, "cancel");
        using var cancellation = new CancellationTokenSource();
        var assets = new ConditionalAssetStore
        {
            BeforeDownload = (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        };
        var encoder = new IdEncoder(new SqidsEncoder<int>());
        var store = new DiscScanComparisonStore(assets);
        var service = new DiscScanComparisonService(factory, assets, encoder, store);
        await SeedInputs(assets, encoder, disc);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            if (batch)
            {
                await service.RecompareAllDiscsAsync(cancellation.Token);
            }
            else
            {
                await service.RecompareDiscAsync(disc.Id, cancellation.Token);
            }
        });

        await Assert.That(assets.WriteAttempts).IsEqualTo(0);
        await Assert.That(await ReadHistory(store, encoder, disc)).IsNull();
    }

    private static async Task<UserContributionDisc> SeedDisc(SqlServerDataContext db, string userAgent)
    {
        var disc = new UserContributionDisc
        {
            Format = "blu-ray",
            Name = "Test disc",
            Slug = "test-disc",
            ManifestUserAgent = userAgent,
            UserContribution = new UserContribution
            {
                UserId = "test-user",
                Created = DateTimeOffset.UtcNow,
                ExternalProvider = "TMDB",
                ExternalId = "123",
                Upc = "123456789012",
                ReleaseSlug = "test-release"
            }
        };
        db.UserContributionDiscs.Add(disc);
        await db.SaveChangesAsync();
        return disc;
    }

    private static async Task<ComparisonInputs> SeedInputs(
        ConditionalAssetStore assets, IdEncoder encoder, UserContributionDisc disc,
        bool includeLog = true, bool includeManifest = true, bool malformedManifest = false)
    {
        // Keep original line endings, formatting, non-ASCII UTF8 and trailing whitespace.
        // Hashing parsed/reformatted JSON or normalized log lines must not produce the expected hash.
        string logText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-disc-for-odm.txt"));
        byte[] log = Encoding.UTF8.GetBytes(logText.ReplaceLineEndings("\r\n") + "\r\n \r\n");
        string manifestText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "sample-disc.odm.json"));
        byte[] manifest = malformedManifest
            ? "not json"u8.ToArray()
            : Encoding.UTF8.GetBytes(manifestText.Replace("\"name\": \"thediscdb\"", "\"name\": \"thediscdb-é\"") + "\r\n \t");
        string contributionId = encoder.Encode(disc.UserContribution.Id);
        string discId = encoder.Encode(disc.Id);
        if (includeLog)
        {
            using var stream = new MemoryStream(log);
            await assets.Save(stream, ContributionDiscAssets.LogsPath(contributionId, discId), ContentTypes.TextContentType);
        }
        if (includeManifest)
        {
            using var stream = new MemoryStream(manifest);
            await assets.Save(stream, ContributionDiscAssets.ManifestPath(contributionId, discId), ContentTypes.JsonContentType);
        }
        return new ComparisonInputs(log, manifest);
    }

    private static Task<DiscScanComparisonFile?> ReadHistory(DiscScanComparisonStore store, IdEncoder encoder, UserContributionDisc disc)
        => store.ReadAsync(encoder.Encode(disc.UserContribution.Id), encoder.Encode(disc.Id));

    private sealed record ComparisonInputs(byte[] Log, byte[] Manifest);

    private sealed class InMemoryContextFactory : IDbContextFactory<SqlServerDataContext>
    {
        private readonly DbContextOptions<SqlServerDataContext> options =
            new DbContextOptionsBuilder<SqlServerDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

        public SqlServerDataContext CreateDbContext() => new(this.options);
    }
}
