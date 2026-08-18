namespace TheDiscDb.Services.Contributions;

using System.Data;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb.Data.Import;
using TheDiscDb.Web.Data;

public sealed class IntakeMatchingService(
    SqlServerDataContext database,
    IStaticAssetStore assetStore,
    SqidsEncoder<int> idEncoder) : IIntakeMatchingService
{
    public async Task<IntakeReleaseMatch?> FindReleaseMatchAsync(
        string externalProvider,
        string externalId,
        string upc,
        CancellationToken cancellationToken = default)
    {
        var provider = IntakeMatchNormalizer.ExternalProvider(externalProvider);
        var normalizedExternalId = IntakeMatchNormalizer.ExternalId(externalId);
        var normalizedUpc = IntakeMatchNormalizer.Upc(upc);
        if (provider != "TMDB" || normalizedExternalId.Length == 0 || normalizedUpc.Length == 0)
        {
            return null;
        }

        var release = await database.IntakeReleases
            .AsNoTracking()
            .Include(r => r.Discs)
                .ThenInclude(rd => rd.Disc)
                    .ThenInclude(d => d.Evidence)
            .Where(r =>
                r.Status != IntakeStatus.Rejected &&
                r.ExternalProvider == provider &&
                r.ExternalId == normalizedExternalId &&
                r.Upc == normalizedUpc)
            .OrderByDescending(r => r.Status == IntakeStatus.ReadyForReview)
            .ThenByDescending(r => r.UpdatedAt ?? r.ReceivedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (release is null)
        {
            return null;
        }

        var releaseEvidence = release.Discs
            .SelectMany(rd => rd.Disc.Evidence)
            .OrderByDescending(e => e.RecordedAt)
            .ThenByDescending(e => e.Id)
            .ToList();
        var frontImageLocation = release.FrontImageLocation ??
            releaseEvidence.FirstOrDefault(e => e.Type == IntakeDiscEvidenceType.FrontImage)?.Location;
        var backImageLocation =
            releaseEvidence.FirstOrDefault(e => e.Type == IntakeDiscEvidenceType.BackImage)?.Location;

        return new IntakeReleaseMatch(
                release.Asin,
                release.Upc,
                release.ReleaseDate,
                release.ReleaseTitle,
                release.ReleaseSlug,
                release.Locale,
                release.RegionCode,
                ToImageUrl(frontImageLocation),
                ToImageUrl(backImageLocation));
    }

    public async Task<IntakeDiscMatch?> FindDiscMatchAsync(
        int contributionId,
        string userId,
        string contentHash,
        string? format,
        string? globalDiscId,
        CancellationToken cancellationToken = default)
    {
        if (!await database.UserContributions.AnyAsync(
                c => c.Id == contributionId && c.UserId == userId,
                cancellationToken))
        {
            return null;
        }

        var match = await LoadDiscMatchAsync(
            contributionId,
            contentHash,
            format,
            globalDiscId,
            cancellationToken);
        return match is null ? null : ToDiscMatch(match, globalDiscId);
    }

    public async Task<IntakeDiscPromotionResult?> PromoteDiscAsync(
        int contributionId,
        string userId,
        IntakeDiscPromotionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        // Serialize promotions per contribution so concurrent requests cannot create competing discs or logs.
        var contributionQuery = database.Database.IsRelational()
            ? database.UserContributions.FromSqlInterpolated(
                $"SELECT * FROM [UserContributions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {contributionId}")
            : database.UserContributions;
        var contribution = await contributionQuery
            .Include(c => c.Discs)
            .Include(c => c.Boxset)
                .ThenInclude(b => b!.Members)
            .FirstOrDefaultAsync(
                c => c.UserId == userId,
                cancellationToken);
        if (contribution is null || !contribution.Status.IsEditableByOwner())
        {
            return null;
        }

        var normalizedHash = IntakeMatchNormalizer.ContentHash(request.ContentHash);
        var normalizedFormat = IntakeMatchNormalizer.Format(request.Format);
        var publishedFormats = await database.Discs
            .Where(d => d.ContentHash != null && d.ContentHash.ToUpper() == normalizedHash)
            .Select(d => d.Format)
            .ToListAsync(cancellationToken);
        var mainDatabaseMatch = publishedFormats.Any(
            format => IntakeMatchNormalizer.Format(format) == normalizedFormat);
        if (mainDatabaseMatch)
        {
            return new IntakeDiscPromotionResult(
                new UserContributionDisc(),
                false,
                false,
                null,
                false,
                true);
        }

        var match = await LoadDiscMatchAsync(
            contributionId,
            normalizedHash,
            request.Format,
            request.GlobalDiscId,
            cancellationToken);
        if (match is null)
        {
            return null;
        }

        var submittedGlobalDiscId = NullIfEmpty(IntakeMatchNormalizer.GlobalDiscId(request.GlobalDiscId));
        var existingPromotion = await database.IntakePromotions
            .Where(p =>
                p.Target == IntakePromotionTarget.Disc &&
                p.IntakeDiscId == match.Disc.Id &&
                p.UserContributionId == contributionId)
            .OrderByDescending(p => p.Status == IntakePromotionStatus.Completed)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var disc = contribution.Discs.FirstOrDefault(
            d => IntakeMatchNormalizer.ContentHash(d.ContentHash) == normalizedHash
                && IntakeMatchNormalizer.Format(d.Format) == normalizedFormat);
        if (disc is null)
        {
            disc = new UserContributionDisc
            {
                ContentHash = normalizedHash,
                Index = contribution.Discs.Count == 0
                    ? 1
                    : contribution.Discs.Max(d => d.Index ?? 0) + 1,
                ExistingDiscPath = string.Empty,
            };
            contribution.Discs.Add(disc);

            if (contribution.Boxset is not null && contribution.Boxset.Status.IsEditableByOwner())
            {
                var maxSortOrder = contribution.Boxset.Members.Count == 0
                    ? -1
                    : contribution.Boxset.Members.Max(m => m.SortOrder);
                contribution.Boxset.Members.Add(new UserContributionBoxsetMember
                {
                    Boxset = contribution.Boxset,
                    Disc = disc,
                    SortOrder = maxSortOrder + 1,
                });
            }
        }

        disc.Format = IntakeMatchNormalizer.Format(
            FirstNonEmpty(request.Format, disc.Format, match.Disc.Format));
        disc.Name = FirstNonEmpty(request.Name, disc.Name, match.ReleaseDisc?.Name, $"Disc {disc.Index:D2}");
        disc.Slug = FirstNonEmpty(request.Slug, disc.Slug, match.ReleaseDisc?.Slug, $"disc{disc.Index:D2}");
        if (submittedGlobalDiscId is not null)
        {
            disc.GlobalDiscId = submittedGlobalDiscId;
        }

        await database.SaveChangesAsync(cancellationToken);

        var selectedEvidenceSet = match.EvidenceSet;
        var logPath = GetContributionLogPath(contribution.Id, disc.Id);
        var stagedLogPath = GetStagedIntakeLogPath(contribution.Id, match.Disc.Id);
        var logExisted = await assetStore.Exists(logPath, cancellationToken);
        var stagedLogExists = await assetStore.Exists(stagedLogPath, cancellationToken);
        string? failureReason = null;
        var promotedSource = selectedEvidenceSet.ScanLogs.FirstOrDefault()?.Source ?? IntakeSource.Unknown;
        if (!logExisted && !stagedLogExists)
        {
            var copyResult = await CopyBestScanLogAsync(
                match.EvidenceSets,
                stagedLogPath,
                cancellationToken);
            stagedLogExists = copyResult.Copied;
            failureReason = copyResult.FailureReason;
            promotedSource = copyResult.Source;
            selectedEvidenceSet = copyResult.EvidenceSet ?? selectedEvidenceSet;
        }

        var evidenceGlobalDiscId = selectedEvidenceSet.GlobalDiscId;
        var mismatch = submittedGlobalDiscId is not null && !selectedEvidenceSet.ExactGlobalDiscIdMatch;
        disc.LogsUploaded = logExisted;
        disc.LogUploadError = logExisted ? null : failureReason;

        var promotion = existingPromotion;
        if (promotion is null)
        {
            promotion = new IntakePromotion
            {
                Target = IntakePromotionTarget.Disc,
                Source = promotedSource,
                CreatedAt = DateTimeOffset.UtcNow,
                RequestedByUserId = userId,
                IntakeDiscId = match.Disc.Id,
                UserContributionId = contribution.Id,
                MediaItemSlug = match.ReleaseDisc?.Release.MediaItemSlug,
                BoxsetSlug = match.ReleaseDisc?.Release.BoxsetSlug,
                ReleaseSlug = match.ReleaseDisc?.Release.ReleaseSlug,
                DiscFormat = disc.Format,
                DiscContentHash = disc.ContentHash,
                DiscGlobalId = submittedGlobalDiscId,
                EvidenceSetId = selectedEvidenceSet.Id,
            };
            database.IntakePromotions.Add(promotion);
        }

        var preserveCompletedPromotion = existingPromotion?.Status == IntakePromotionStatus.Completed;
        if (!preserveCompletedPromotion)
        {
            promotion.Status = logExisted
                ? IntakePromotionStatus.Completed
                : stagedLogExists
                    ? IntakePromotionStatus.Pending
                    : IntakePromotionStatus.Failed;
            promotion.CompletedAt = logExisted ? promotion.CompletedAt ?? DateTimeOffset.UtcNow : null;
            promotion.FailureReason = logExisted || stagedLogExists ? null : failureReason;
        }
        await database.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }

        await using var finalizeTransaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        if (finalizeTransaction is not null)
        {
            await database.UserContributions
                .FromSqlInterpolated(
                    $"SELECT * FROM [UserContributions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {contributionId}")
                .Where(c => c.UserId == userId)
                .Select(c => c.Id)
                .SingleAsync(cancellationToken);
        }

        logExisted = await assetStore.Exists(logPath, cancellationToken);
        stagedLogExists = await assetStore.Exists(stagedLogPath, cancellationToken);
        if (logExisted)
        {
            disc.LogsUploaded = true;
            disc.LogUploadError = null;
            promotion.Status = IntakePromotionStatus.Completed;
            promotion.CompletedAt ??= DateTimeOffset.UtcNow;
            promotion.FailureReason = null;
            await database.SaveChangesAsync(cancellationToken);
            if (finalizeTransaction is not null)
            {
                await finalizeTransaction.CommitAsync(cancellationToken);
            }

            if (stagedLogExists)
            {
                await assetStore.Delete(stagedLogPath, CancellationToken.None);
            }

            return new IntakeDiscPromotionResult(
                disc,
                existingPromotion is null,
                true,
                evidenceGlobalDiscId,
                mismatch,
                false);
        }

        if (!stagedLogExists)
        {
            return new IntakeDiscPromotionResult(
                disc,
                false,
                false,
                evidenceGlobalDiscId,
                mismatch,
                false);
        }

        var (finalized, finalizeFailure) = await FinalizeStagedLogAsync(
            stagedLogPath,
            logPath,
            cancellationToken);
        disc.LogsUploaded = finalized;
        disc.LogUploadError = finalizeFailure;
        if (!preserveCompletedPromotion || finalized)
        {
            promotion.Status = finalized ? IntakePromotionStatus.Completed : IntakePromotionStatus.Failed;
            promotion.CompletedAt = finalized ? DateTimeOffset.UtcNow : null;
            promotion.FailureReason = finalizeFailure;
        }
        await database.SaveChangesAsync(cancellationToken);
        if (finalizeTransaction is not null)
        {
            await finalizeTransaction.CommitAsync(cancellationToken);
        }

        if (finalized)
        {
            await assetStore.Delete(stagedLogPath, CancellationToken.None);
        }

        return new IntakeDiscPromotionResult(
            disc,
            existingPromotion is null && finalized,
            finalized,
            evidenceGlobalDiscId,
            mismatch,
            false);
    }

    public async Task<bool> RecordReleasePromotionAsync(
        int contributionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var contribution = await database.UserContributions
            .FirstOrDefaultAsync(
                c => c.Id == contributionId && c.UserId == userId,
                cancellationToken);
        if (contribution is null || !contribution.Status.IsEditableByOwner())
        {
            return false;
        }

        var provider = IntakeMatchNormalizer.ExternalProvider(contribution.ExternalProvider);
        var externalId = IntakeMatchNormalizer.ExternalId(contribution.ExternalId);
        var upc = IntakeMatchNormalizer.Upc(contribution.Upc);
        var release = await database.IntakeReleases
            .Where(r =>
                r.Status != IntakeStatus.Rejected &&
                r.ExternalProvider == provider &&
                r.ExternalId == externalId &&
                r.Upc == upc)
            .OrderByDescending(r => r.Status == IntakeStatus.ReadyForReview)
            .ThenByDescending(r => r.UpdatedAt ?? r.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (release is null)
        {
            return false;
        }

        var alreadyRecorded = await database.IntakePromotions.AnyAsync(p =>
            p.Target == IntakePromotionTarget.Release &&
            p.Status == IntakePromotionStatus.Completed &&
            p.IntakeReleaseId == release.Id &&
            p.UserContributionId == contribution.Id,
            cancellationToken);
        if (alreadyRecorded)
        {
            return true;
        }

        database.IntakePromotions.Add(new IntakePromotion
        {
            Target = IntakePromotionTarget.Release,
            Status = IntakePromotionStatus.Completed,
            Source = release.Source,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            RequestedByUserId = userId,
            IntakeReleaseId = release.Id,
            UserContributionId = contribution.Id,
            MediaItemSlug = release.MediaItemSlug,
            BoxsetSlug = release.BoxsetSlug,
            ReleaseSlug = contribution.ReleaseSlug,
        });
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<LoadedDiscMatch?> LoadDiscMatchAsync(
        int contributionId,
        string contentHash,
        string? format,
        string? globalDiscId,
        CancellationToken cancellationToken)
    {
        var normalizedHash = IntakeMatchNormalizer.ContentHash(contentHash);
        if (normalizedHash.Length == 0)
        {
            return null;
        }

        var contribution = await database.UserContributions
            .AsNoTracking()
            .Where(c => c.Id == contributionId)
            .Select(c => new
            {
                Provider = c.ExternalProvider,
                c.ExternalId,
                c.Upc,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (contribution is null)
        {
            return null;
        }

        var normalizedFormat = IntakeMatchNormalizer.Format(format);
        var discs = await database.IntakeDiscs
            .AsNoTracking()
            .Include(d => d.Evidence)
            .Include(d => d.Releases)
                .ThenInclude(rd => rd.Release)
            .Where(d =>
                d.Status != IntakeStatus.Rejected &&
                d.ContentHash.ToUpper() == normalizedHash)
            .ToListAsync(cancellationToken);

        var normalizedGlobalDiscId = IntakeMatchNormalizer.GlobalDiscId(globalDiscId);
        var candidate = discs
            .Where(d =>
                normalizedFormat.Length == 0
                || IntakeMatchNormalizer.Format(d.Format) == normalizedFormat)
            .Select(d => new
            {
                Disc = d,
                EvidenceSets = SelectEvidenceSets(d.Evidence, normalizedGlobalDiscId),
            })
            .Where(x => x.EvidenceSets.Count > 0)
            .OrderByDescending(x => x.EvidenceSets[0].ExactGlobalDiscIdMatch)
            .ThenByDescending(x => IntakeMatchNormalizer.Format(x.Disc.Format) == normalizedFormat)
            .ThenByDescending(x => x.Disc.Status == IntakeStatus.ReadyForReview)
            .ThenByDescending(x => x.Disc.UpdatedAt ?? x.Disc.ReceivedAt)
            .FirstOrDefault();
        if (candidate is null)
        {
            return null;
        }

        var disc = candidate.Disc;
        var provider = IntakeMatchNormalizer.ExternalProvider(contribution.Provider);
        var externalId = IntakeMatchNormalizer.ExternalId(contribution.ExternalId);
        var upc = IntakeMatchNormalizer.Upc(contribution.Upc);
        var releaseDisc = disc.Releases
            .OrderByDescending(rd =>
                rd.Release.ExternalProvider == provider &&
                rd.Release.ExternalId == externalId &&
                rd.Release.Upc == upc)
            .ThenByDescending(rd => rd.AddedAt)
            .FirstOrDefault();

        return new LoadedDiscMatch(disc, releaseDisc, candidate.EvidenceSets);
    }

    private static IntakeDiscMatch ToDiscMatch(LoadedDiscMatch match, string? submittedGlobalDiscId) =>
        new(
            match.Disc.ContentHash,
            match.Disc.Format,
            match.ReleaseDisc?.Name,
            match.ReleaseDisc?.Slug,
            match.EvidenceSet.GlobalDiscId,
            IntakeMatchNormalizer.GlobalDiscId(submittedGlobalDiscId).Length > 0 &&
                !match.EvidenceSet.ExactGlobalDiscIdMatch,
            match.EvidenceSet.ScanLogs.Count > 0);

    private static IReadOnlyList<SelectedEvidenceSet> SelectEvidenceSets(
        IEnumerable<IntakeDiscEvidence> evidence,
        string normalizedGlobalDiscId)
    {
        return evidence
            .Where(e => !string.IsNullOrWhiteSpace(e.EvidenceSetId))
            .GroupBy(e => e.EvidenceSetId, StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedEvidence = group
                    .OrderByDescending(e => e.RecordedAt)
                    .ThenByDescending(e => e.Id)
                    .ToList();
                var scanLogs = orderedEvidence
                    .Where(e => e.Type == IntakeDiscEvidenceType.ScanLog)
                    .OrderByDescending(e => string.Equals(
                        e.ContentType,
                        ContentTypes.TextContentType,
                        StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(e => !string.IsNullOrWhiteSpace(e.Sha256))
                    .ThenByDescending(e => e.RecordedAt)
                    .ThenByDescending(e => e.Id)
                    .ToList();
                var evidenceGlobalDiscIds = orderedEvidence
                    .Select(e => IntakeMatchNormalizer.GlobalDiscId(e.GlobalDiscId))
                    .Where(id => id.Length > 0)
                    .ToList();
                var exactGlobalDiscIdMatch = normalizedGlobalDiscId.Length > 0 &&
                    evidenceGlobalDiscIds.Contains(normalizedGlobalDiscId, StringComparer.Ordinal);
                var evidenceGlobalDiscId = exactGlobalDiscIdMatch
                    ? normalizedGlobalDiscId
                    : evidenceGlobalDiscIds.FirstOrDefault();

                return new SelectedEvidenceSet(
                    group.Key,
                    evidenceGlobalDiscId,
                    exactGlobalDiscIdMatch,
                    orderedEvidence[0].RecordedAt,
                    scanLogs);
            })
            .Where(set => set.ScanLogs.Count > 0)
            .OrderByDescending(set => set.ExactGlobalDiscIdMatch)
            .ThenByDescending(set => set.RecordedAt)
            .ThenByDescending(set => set.Id, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<ScanLogCopyResult> CopyBestScanLogAsync(
        IReadOnlyList<SelectedEvidenceSet> evidenceSets,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string? lastFailure = null;
        foreach (var evidenceSet in evidenceSets)
        {
            foreach (var evidence in evidenceSet.ScanLogs)
            {
                try
                {
                    if (!await assetStore.Exists(evidence.Location, cancellationToken))
                    {
                        continue;
                    }

                    var data = await assetStore.Download(evidence.Location, cancellationToken);
                    if (data.ToMemory().IsEmpty)
                    {
                        continue;
                    }

                    using var stream = new MemoryStream(data.ToArray());
                    await assetStore.Save(
                        stream,
                        destinationPath,
                        ContentTypes.TextContentType,
                        cancellationToken);
                    return new ScanLogCopyResult(true, null, evidence.Source, evidenceSet);
                }
                catch (Exception ex)
                {
                    lastFailure = $"Could not copy intake scan log: {ex.Message}";
                }
            }
        }

        return new ScanLogCopyResult(
            false,
            lastFailure ?? "No stored intake scan log was available.",
            evidenceSets.SelectMany(set => set.ScanLogs).FirstOrDefault()?.Source ?? IntakeSource.Unknown,
            null);
    }

    private string GetContributionLogPath(int contributionId, int discId) =>
        $"{idEncoder.Encode(contributionId)}/{idEncoder.Encode(discId)}-logs.txt";

    private string GetStagedIntakeLogPath(int contributionId, int intakeDiscId) =>
        $"{idEncoder.Encode(contributionId)}/intake-{idEncoder.Encode(intakeDiscId)}-logs.pending.txt";

    private async Task<(bool Copied, string? FailureReason)> FinalizeStagedLogAsync(
        string stagedPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await assetStore.Download(stagedPath, cancellationToken);
            if (data.ToMemory().IsEmpty)
            {
                return (false, "The staged intake scan log was empty.");
            }

            using var stream = new MemoryStream(data.ToArray());
            await assetStore.Save(
                stream,
                destinationPath,
                ContentTypes.TextContentType,
                cancellationToken);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"Could not finalize intake scan log: {ex.Message}");
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static string? ToImageUrl(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        return location.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            location.StartsWith("/", StringComparison.Ordinal)
            ? location
            : $"/images/{location.TrimStart('/')}";
    }

    private sealed record LoadedDiscMatch(
        IntakeDisc Disc,
        IntakeReleaseDisc? ReleaseDisc,
        IReadOnlyList<SelectedEvidenceSet> EvidenceSets)
    {
        public SelectedEvidenceSet EvidenceSet => this.EvidenceSets[0];
    }

    private sealed record SelectedEvidenceSet(
        string Id,
        string? GlobalDiscId,
        bool ExactGlobalDiscIdMatch,
        DateTimeOffset RecordedAt,
        IReadOnlyList<IntakeDiscEvidence> ScanLogs);

    private sealed record ScanLogCopyResult(
        bool Copied,
        string? FailureReason,
        IntakeSource Source,
        SelectedEvidenceSet? EvidenceSet);
}
