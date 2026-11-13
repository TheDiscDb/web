using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;

namespace TheDiscDb.UnitTests.Services;

public class DiscScanComparisonStoreTests
{
    [Test]
    public async Task AppendAsync_AppendsTwiceDespiteCreateOnlySave()
    {
        var assets = new ConditionalAssetStore();
        string path = ContributionDiscAssets.ComparisonPath("contribution", "disc");
        var original = new DiscScanComparisonFile { Comparisons = [Record("original")] };
        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DiscScanComparisonStore.Serialize(original))))
        {
            await assets.Save(stream, path, ContentTypes.JsonContentType);
        }

        // Model the production Save behavior: attempting replacement must leave the original intact.
        using (var stream = new MemoryStream("{}"u8.ToArray()))
        {
            await assets.Save(stream, path, ContentTypes.JsonContentType);
        }
        await Assert.That(DiscScanComparisonStore.Deserialize((await assets.Download(path)).ToString())!.Comparisons[0].ProducerVersion).IsEqualTo("original");

        var store = new DiscScanComparisonStore(assets);
        await store.AppendAsync("contribution", "disc", Record("first"));
        await store.AppendAsync("contribution", "disc", Record("second"));

        var persisted = (await store.ReadAsync("contribution", "disc"))!;
        await Assert.That(persisted.Comparisons.Count).IsEqualTo(3);
        await Assert.That(persisted.Comparisons[0].ProducerVersion).IsEqualTo("second");
        await Assert.That(persisted.Comparisons[1].ProducerVersion).IsEqualTo("first");
        await Assert.That(persisted.Comparisons[2].ProducerVersion).IsEqualTo("original");
        await Assert.That(assets.SaveCount).IsEqualTo(2);
        await Assert.That(assets.WriteAttempts).IsEqualTo(2);
    }

    [Test]
    public async Task AppendAsync_RetainsNewestTwentyPersistedRecords()
    {
        var assets = new ConditionalAssetStore();
        var store = new DiscScanComparisonStore(assets);
        for (int i = 0; i < 25; i++)
        {
            await store.AppendAsync("contribution", "disc", Record(i.ToString()));
        }

        var persisted = (await store.ReadAsync("contribution", "disc"))!;
        await Assert.That(persisted.Comparisons.Count).IsEqualTo(20);
        for (int i = 0; i < 20; i++)
        {
            await Assert.That(persisted.Comparisons[i].ProducerVersion).IsEqualTo((24 - i).ToString());
        }
        await Assert.That(assets.SaveCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AppendAsync_ConcurrentAppendsRetryWithoutLosingRecords(bool historyExists)
    {
        var assets = new ConditionalAssetStore { ForceConcurrentReads = true };
        if (historyExists)
        {
            string json = DiscScanComparisonStore.Serialize(new DiscScanComparisonFile { Comparisons = [Record("original")] });
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await assets.Save(stream, ContributionDiscAssets.ComparisonPath("contribution", "disc"), ContentTypes.JsonContentType);
        }

        // Separate store instances ensure correctness does not depend on a process-local lock.
        var first = new DiscScanComparisonStore(assets);
        var second = new DiscScanComparisonStore(assets);
        await Task.WhenAll(
            first.AppendAsync("contribution", "disc", Record("first")),
            second.AppendAsync("contribution", "disc", Record("second")));

        var persisted = (await first.ReadAsync("contribution", "disc"))!;
        await Assert.That(persisted.Comparisons.Count).IsEqualTo(historyExists ? 3 : 2);
        await Assert.That(persisted.Comparisons.Count(record => record.ProducerVersion == "first")).IsEqualTo(1);
        await Assert.That(persisted.Comparisons.Count(record => record.ProducerVersion == "second")).IsEqualTo(1);
        if (historyExists)
        {
            await Assert.That(persisted.Comparisons[2].ProducerVersion).IsEqualTo("original");
        }
        await Assert.That(assets.ConflictCount).IsEqualTo(1);
        await Assert.That(assets.WriteAttempts).IsEqualTo(3);
        await Assert.That(assets.ReadCount).IsEqualTo(3);
    }

    [Test]
    public async Task AppendAsync_ConflictRetriesAreBounded()
    {
        var assets = new ConditionalAssetStore { AlwaysConflict = true };
        var store = new DiscScanComparisonStore(assets);

        await Assert.ThrowsAsync<IOException>(() => store.AppendAsync("contribution", "disc", Record("first")));

        await Assert.That(assets.WriteAttempts).IsEqualTo(5);
        await Assert.That(assets.ReadCount).IsEqualTo(5);
        await Assert.That(await store.ReadAsync("contribution", "disc")).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AppendAsync_PropagatesActualStorageFailuresWithoutRetry(bool failRead)
    {
        var failure = new IOException("Storage unavailable");
        var assets = new ConditionalAssetStore
        {
            ReadFailure = failRead ? failure : null,
            WriteFailure = failRead ? null : failure
        };
        var store = new DiscScanComparisonStore(assets);

        var exception = await Assert.ThrowsAsync<IOException>(() => store.AppendAsync("contribution", "disc", Record("first")));

        await Assert.That(ReferenceEquals(exception, failure)).IsTrue();
        await Assert.That(assets.ReadCount).IsEqualTo(1);
        await Assert.That(assets.WriteAttempts).IsEqualTo(failRead ? 0 : 1);
        await Assert.That(await store.ReadAsync("contribution", "disc")).IsNull();
    }

    [Test]
    public async Task AppendAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var assets = new ConditionalAssetStore();
        var store = new DiscScanComparisonStore(assets);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.AppendAsync("contribution", "disc", Record("first"), cancellation.Token));

        await Assert.That(assets.ReadCount).IsEqualTo(0);
        await Assert.That(assets.WriteAttempts).IsEqualTo(0);
    }

    [Test]
    public async Task AppendAsync_LeavesInvalidExistingHistoryUntouched()
    {
        var assets = new ConditionalAssetStore();
        string path = ContributionDiscAssets.ComparisonPath("contribution", "disc");
        using var stream = new MemoryStream("not json"u8.ToArray());
        await assets.Save(stream, path, ContentTypes.JsonContentType);
        var store = new DiscScanComparisonStore(assets);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AppendAsync("contribution", "disc", Record("first")));

        await Assert.That((await assets.Download(path)).ToString()).IsEqualTo("not json");
        await Assert.That(assets.WriteAttempts).IsEqualTo(0);
    }

    [Test]
    public async Task Serialize_RoundTripsOptionalProvenanceWithoutChangingHistoricalJson()
    {
        var record = Record("first");
        var file = new DiscScanComparisonFile { Comparisons = [record] };
        await Assert.That(DiscScanComparisonStore.Serialize(file).Contains("\"comparerVersion\"")).IsFalse();
        record.ComparerVersion = "comparer-v1";
        record.ManifestSha256 = "manifest-hash";
        record.ReferenceSha256 = "reference-hash";

        var restored = DiscScanComparisonStore.Deserialize(DiscScanComparisonStore.Serialize(file))!.Comparisons[0];
        await Assert.That(restored.ComparerVersion).IsEqualTo("comparer-v1");
        await Assert.That(restored.ManifestSha256).IsEqualTo("manifest-hash");
        await Assert.That(restored.ReferenceSha256).IsEqualTo("reference-hash");
    }

    private static DiscScanComparisonRecord Record(string version) => new()
    {
        ProducerVersion = version,
        ComparedAt = DateTimeOffset.UtcNow,
        Status = DiscLogManifestComparisonStatus.Match
    };

    [Test]
    public async Task Serialize_RoundTripsComparisonHistoryAsReadableJson()
    {
        var file = new DiscScanComparisonFile
        {
            Comparisons =
            [
                new DiscScanComparisonRecord
                {
                    ComparedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    Status = DiscLogManifestComparisonStatus.Mismatch,
                    Format = "blu-ray",
                    ProducerVersion = "1.0",
                    LogTitleCount = 5,
                    ManifestTitleCount = 6,
                    MatchedTitleCount = 5,
                    Differences = new DiscLogManifestDifferences
                    {
                        Error = "boom",
                        IdentifyExperience = IdentifyExperienceComparison.NotComparable("boom")
                    }
                }
            ]
        };

        string json = DiscScanComparisonStore.Serialize(file);
        var roundTripped = DiscScanComparisonStore.Deserialize(json);

        await Assert.That(json).Contains("\"status\": \"Mismatch\"");
        await Assert.That(json).Contains("\"error\": \"boom\"");
        await Assert.That(roundTripped).IsNotNull();
        await Assert.That(roundTripped!.Comparisons.Count).IsEqualTo(1);
        await Assert.That(roundTripped.Comparisons[0].Status).IsEqualTo(DiscLogManifestComparisonStatus.Mismatch);
        await Assert.That(roundTripped.Comparisons[0].ManifestTitleCount).IsEqualTo(6);
        await Assert.That(roundTripped.Comparisons[0].Differences.Error).IsEqualTo("boom");
        await Assert.That(roundTripped.Comparisons[0].Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.NotComparable);
    }

    [Test]
    public async Task Deserialize_ReturnsNullForInvalidJson()
    {
        await Assert.That(DiscScanComparisonStore.Deserialize("not json")).IsNull();
        await Assert.That(DiscScanComparisonStore.Deserialize("")).IsNull();
    }

    [Test]
    public async Task Serialize_PreservesGradedCountsAndOptionalHistoricalScores()
    {
        var disc = new MakeMkv.DiscInfo();
        disc.Titles.Add(new MakeMkv.Title { Playlist = "1" });
        var measured = IdentifyExperienceComparer.Compare(disc, disc);
        var file = new DiscScanComparisonFile
        {
            Comparisons =
            [
                new DiscScanComparisonRecord { Differences = new() { IdentifyExperience = measured } },
                new DiscScanComparisonRecord { Differences = new() { IdentifyExperience = measured with { Similarity = null } } }
            ]
        };
        var restored = DiscScanComparisonStore.Deserialize(DiscScanComparisonStore.Serialize(file))!;
        var score = restored.Comparisons[0].Differences.IdentifyExperience!.Similarity!;
        await Assert.That(score.Overall).IsEqualTo(100d);
        await Assert.That(score.RowPresentation.Matching).IsEqualTo(7L);
        await Assert.That(score.Ordering.Total).IsEqualTo(1L);
        await Assert.That(restored.Comparisons[1].Differences.IdentifyExperience!.Status).IsEqualTo(IdentifyExperienceStatus.Match);
        await Assert.That(restored.Comparisons[1].Differences.IdentifyExperience!.Similarity).IsNull();
    }

    internal sealed class ConditionalAssetStore : IStaticAssetStore
    {
        private readonly object gate = new();
        private readonly Dictionary<string, MutableAssetSnapshot> assets = [];
        private readonly TaskCompletionSource firstReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int version;

        public string ContainerName { get; set; } = "tests";
        public bool ForceConcurrentReads { get; init; }
        public bool AlwaysConflict { get; init; }
        public Exception? ReadFailure { get; init; }
        public Exception? WriteFailure { get; init; }
        public Func<string, Exception?>? WriteFailureForPath { get; init; }
        public Action<string, CancellationToken>? BeforeDownload { get; init; }
        public int SaveCount { get; private set; }
        public int ReadCount { get; private set; }
        public int WriteAttempts { get; private set; }
        public int ConflictCount { get; private set; }

        public async Task<MutableAssetSnapshot?> ReadForUpdate(string remotePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MutableAssetSnapshot? snapshot;
            bool waitForCompetingRead;
            lock (this.gate)
            {
                this.ReadCount++;
                if (this.ReadFailure is not null)
                {
                    throw this.ReadFailure;
                }

                this.assets.TryGetValue(remotePath, out snapshot);
                waitForCompetingRead = this.ForceConcurrentReads && this.ReadCount <= 2;
                if (this.ReadCount == 2)
                {
                    this.firstReads.TrySetResult();
                }
            }

            if (waitForCompetingRead)
            {
                await this.firstReads.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return snapshot;
        }

        public Task<bool> TryWrite(BinaryData content, string remotePath, string contentType, string? expectedETag, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (this.gate)
            {
                this.WriteAttempts++;
                var failure = this.WriteFailure ?? this.WriteFailureForPath?.Invoke(remotePath);
                if (failure is not null)
                {
                    throw failure;
                }

                this.assets.TryGetValue(remotePath, out var current);
                if (this.AlwaysConflict || current?.ETag != expectedETag)
                {
                    this.ConflictCount++;
                    return Task.FromResult(false);
                }

                this.assets[remotePath] = new MutableAssetSnapshot(content, (++this.version).ToString());
                return Task.FromResult(true);
            }
        }

        public async Task<string> Save(Stream stream, string remotePath, string contentType, CancellationToken cancellationToken = default)
        {
            var content = await BinaryData.FromStreamAsync(stream, cancellationToken);
            lock (this.gate)
            {
                this.SaveCount++;
                if (!this.assets.ContainsKey(remotePath))
                {
                    this.assets[remotePath] = new MutableAssetSnapshot(content, (++this.version).ToString());
                }
            }
            return remotePath;
        }

        public async Task<string> Save(string filePath, string remotePath, string contentType, CancellationToken cancellationToken = default)
        {
            using var stream = File.OpenRead(filePath);
            return await this.Save(stream, remotePath, contentType, cancellationToken);
        }

        public Task<bool> Exists(string remotePath, CancellationToken cancellationToken = default)
        {
            lock (this.gate)
            {
                return Task.FromResult(this.assets.ContainsKey(remotePath));
            }
        }

        public Task<BinaryData> Download(string remotePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.BeforeDownload?.Invoke(remotePath, cancellationToken);
            lock (this.gate)
            {
                return Task.FromResult(this.assets[remotePath].Content);
            }
        }

        public Task Delete(string remotePath, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("History updates must not delete existing assets.");
    }
}
