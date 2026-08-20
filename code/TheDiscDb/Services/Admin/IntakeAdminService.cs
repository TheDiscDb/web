namespace TheDiscDb.Services.Admin;

using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.InputModels;
using TheDiscDb.Validation;
using TheDiscDb.Web.Data;

public interface IIntakeAdminService
{
    Task<IntakeReleasePage> GetReleasesAsync(IntakeReleaseSearch request, CancellationToken cancellationToken = default);
    Task<IntakeReleaseDetails?> GetReleaseAsync(int id, CancellationToken cancellationToken = default);
    Task<IntakeAdminResult> UpdateReleaseAsync(int id, IntakeReleaseEditRequest request, CancellationToken cancellationToken = default);
    Task<IntakeAdminResult> UpdateDiscsAsync(int id, IReadOnlyList<IntakeDiscLinkEditRequest> request, bool confirmSharedFormatChange, CancellationToken cancellationToken = default);
    Task<IntakeAdminResult> ReplaceImageAsync(int id, IntakeImageSide side, Stream content, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<IntakeAdminResult> DeleteImageAsync(int id, IntakeImageSide side, CancellationToken cancellationToken = default);
    Task<IntakeDeleteResult> DeleteReleaseAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class IntakeAdminService : IIntakeAdminService
{
    private const long MaxImageBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
    };

    private static readonly HashSet<string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    private readonly SqlServerDataContext database;
    private readonly IStaticAssetStore assetStore;
    private readonly IStaticAssetStore imageStore;
    private readonly ILogger<IntakeAdminService> logger;

    public IntakeAdminService(
        SqlServerDataContext database,
        IStaticAssetStore assetStore,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        ILogger<IntakeAdminService> logger)
    {
        this.database = database;
        this.assetStore = assetStore;
        this.imageStore = imageStore;
        this.logger = logger;
    }

    public async Task<IntakeReleasePage> GetReleasesAsync(
        IntakeReleaseSearch request,
        CancellationToken cancellationToken = default)
    {
        int pageSize = Math.Clamp(request.PageSize, 1, 100);
        int page = Math.Max(request.Page, 0);
        string? search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        IQueryable<IntakeRelease> query = database.IntakeReleases.AsNoTracking();
        if (request.Status is { } status)
        {
            query = query.Where(release => release.Status == status);
        }

        if (search != null)
        {
            query = query.Where(release =>
                release.ReleaseTitle != null && release.ReleaseTitle.Contains(search) ||
                release.Upc.Contains(search) ||
                release.Asin != null && release.Asin.Contains(search) ||
                release.ExternalId.Contains(search) ||
                release.SourceReleaseId.Contains(search) ||
                release.Discs.Any(link =>
                    link.Name != null && link.Name.Contains(search) ||
                    link.SourceDiscId != null && link.SourceDiscId.Contains(search) ||
                    link.GlobalDiscId != null && link.GlobalDiscId.Contains(search) ||
                    link.Disc.ContentHash.Contains(search)));
        }

        int totalCount = await query.CountAsync(cancellationToken);
        var releases = await query
            .OrderByDescending(release => release.ReceivedAt)
            .ThenByDescending(release => release.Id)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(release => new IntakeReleaseListItem(
                release.Id,
                release.Status,
                release.ReleaseTitle,
                release.ReleaseSlug,
                release.MediaItemSlug,
                release.BoxsetSlug,
                release.ExternalProvider,
                release.ExternalId,
                release.Upc,
                release.ReceivedAt,
                release.Discs.Count))
            .ToListAsync(cancellationToken);

        return new IntakeReleasePage(releases, totalCount, page, pageSize);
    }

    public async Task<IntakeReleaseDetails?> GetReleaseAsync(int id, CancellationToken cancellationToken = default)
    {
        var release = await database.IntakeReleases
            .AsSplitQuery()
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
                    .ThenInclude(disc => disc.Releases)
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
                    .ThenInclude(disc => disc.Evidence)
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
                    .ThenInclude(disc => disc.Promotions)
            .Include(item => item.Promotions)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (release == null)
        {
            return null;
        }

        var details = MapDetails(release);
        database.ChangeTracker.Clear();
        return details;
    }

    public async Task<IntakeAdminResult> UpdateReleaseAsync(
        int id,
        IntakeReleaseEditRequest request,
        CancellationToken cancellationToken = default)
    {
        request.ExternalProvider = IntakeMatchNormalizer.ExternalProvider(request.ExternalProvider);
        request.ExternalId = IntakeMatchNormalizer.ExternalId(request.ExternalId);
        request.Upc = IntakeMatchNormalizer.Upc(request.Upc);
        request.Asin = NormalizeOptional(request.Asin);
        var errors = ValidateRelease(request);
        if (errors.Count != 0)
        {
            return IntakeAdminResult.Failed(errors);
        }

        var release = await database.IntakeReleases.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (release == null)
        {
            return IntakeAdminResult.Failed("Intake release was not found.");
        }

        if (release.Status == IntakeStatus.Promoted && request.Status != IntakeStatus.Promoted)
        {
            return IntakeAdminResult.Failed("Promoted intake releases cannot be demoted.");
        }

        release.Status = request.Status;
        release.ExternalProvider = request.ExternalProvider;
        release.ExternalId = request.ExternalId;
        release.Upc = request.Upc;
        release.Asin = NormalizeOptional(request.Asin)?.ToUpperInvariant();
        release.ReleaseDate = request.ReleaseDate;
        release.ReleaseTitle = NormalizeOptional(request.ReleaseTitle);
        release.ReleaseSlug = NormalizeOptional(request.ReleaseSlug);
        release.MediaItemSlug = NormalizeOptional(request.MediaItemSlug);
        release.BoxsetSlug = NormalizeOptional(request.BoxsetSlug);
        release.Locale = NormalizeOptional(request.Locale);
        release.RegionCode = NormalizeOptional(request.RegionCode);
        release.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);
        return IntakeAdminResult.Success("Release metadata saved.");
    }

    public async Task<IntakeAdminResult> UpdateDiscsAsync(
        int id,
        IReadOnlyList<IntakeDiscLinkEditRequest> request,
        bool confirmSharedFormatChange,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var executionStrategy = database.Database.CreateExecutionStrategy();
            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = database.Database.IsRelational()
                    ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                    : null;

                var release = await LoadReleaseForDiscUpdateAsync(id, cancellationToken);
                if (release == null)
                {
                    return IntakeAdminResult.Failed("Intake release was not found.");
                }

                var errors = ValidateDiscLinks(release, request);
                if (errors.Count != 0)
                {
                    return IntakeAdminResult.Failed(errors);
                }

                var suppliedById = request.ToDictionary(item => item.LinkId);
                var canonicalDiscIds = release.Discs.Select(link => link.IntakeDiscId).Distinct().ToArray();
                await LockCanonicalDiscsAndLinksAsync(canonicalDiscIds, cancellationToken);
                var sharedDiscIds = await database.IntakeReleaseDiscs
                    .AsNoTracking()
                    .Where(link => canonicalDiscIds.Contains(link.IntakeDiscId))
                    .GroupBy(link => link.IntakeDiscId)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToListAsync(cancellationToken);

                bool changesSharedFormat = release.Discs.Any(link =>
                {
                    var update = suppliedById[link.Id];
                    return sharedDiscIds.Contains(link.IntakeDiscId) &&
                        !string.Equals(link.Disc.Format, IntakeMatchNormalizer.Format(update.Format), StringComparison.Ordinal);
                });

                if (changesSharedFormat && !confirmSharedFormatChange)
                {
                    return IntakeAdminResult.Failed(
                        "Changing a shared canonical disc format affects every release linked to that disc. Confirm this change before saving.");
                }

                var linksWithChangedIndexes = release.Discs
                    .Where(link => link.Index != suppliedById[link.Id].Index)
                    .ToList();
                if (linksWithChangedIndexes.Count != 0)
                {
                    foreach (var link in linksWithChangedIndexes)
                    {
                        link.Index = null;
                    }

                    await database.SaveChangesAsync(cancellationToken);
                }

                foreach (var link in release.Discs)
                {
                    var update = suppliedById[link.Id];
                    link.Index = update.Index;
                    link.Name = NormalizeOptional(update.Name);
                    link.Slug = NormalizeOptional(update.Slug);
                    link.Disc.Format = IntakeMatchNormalizer.Format(update.Format);
                    link.Disc.UpdatedAt = DateTimeOffset.UtcNow;
                }

                release.UpdatedAt = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(cancellationToken);

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return IntakeAdminResult.Success("Disc metadata saved.");
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to update intake discs for release {ReleaseId}", id);
            return IntakeAdminResult.Failed("Failed to save disc metadata.");
        }
    }

    public async Task<IntakeAdminResult> ReplaceImageAsync(
        int id,
        IntakeImageSide side,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        string? imageError = ValidateImage(fileName, contentType);
        if (imageError != null)
        {
            return IntakeAdminResult.Failed(imageError);
        }

        byte[] bytes;
        try
        {
            bytes = await ReadImageAsync(content, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return IntakeAdminResult.Failed(exception.Message);
        }

        var release = await database.IntakeReleases
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
                    .ThenInclude(disc => disc.Evidence)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (release == null)
        {
            return IntakeAdminResult.Failed("Intake release was not found.");
        }

        if (side == IntakeImageSide.Back && release.Discs.Count == 0)
        {
            return IntakeAdminResult.Failed("A back image requires at least one linked disc so its evidence can be recorded.");
        }

        string extension = GetImageExtension(fileName, contentType);
        string sideName = side == IntakeImageSide.Front ? "front" : "back";
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string location = $"intake/releases/{release.Id}/{sideName}-{hash}{extension}";

        try
        {
            await using var upload = new MemoryStream(bytes, writable: false);
            await imageStore.Save(upload, location, NormalizeImageContentType(contentType, extension), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to upload intake {Side} image for release {ReleaseId}", sideName, id);
            return IntakeAdminResult.Failed($"Failed to upload the {sideName} image.");
        }

        var warnings = new List<string>();
        var matchingEvidence = GetReleaseOwnedEvidence(release)
            .Where(item => item.Type == ToEvidenceType(side))
            .ToList();
        var previousLocations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evidence in matchingEvidence)
        {
            if (!string.IsNullOrEmpty(evidence.Location))
            {
                previousLocations.Add(evidence.Location);
            }
        }

        if (side == IntakeImageSide.Front)
        {
            if (!string.IsNullOrEmpty(release.FrontImageLocation))
            {
                previousLocations.Add(release.FrontImageLocation);
            }

            release.FrontImageLocation = location;
        }

        if (matchingEvidence.Count != 0)
        {
            foreach (var evidence in matchingEvidence)
            {
                evidence.Location = location;
                evidence.ContentType = NormalizeImageContentType(contentType, extension);
                evidence.Sha256 = hash;
                evidence.RecordedAt = DateTimeOffset.UtcNow;
            }
        }
        else if (release.Discs.OrderBy(item => item.Index).FirstOrDefault() is { } link)
        {
            link.Disc.Evidence.Add(new IntakeDiscEvidence
            {
                Source = release.Source,
                SourceEvidenceId = release.SourceReleaseId,
                EvidenceSetId = release.SourceReleaseId,
                GlobalDiscId = link.GlobalDiscId,
                Type = ToEvidenceType(side),
                Location = location,
                ContentType = NormalizeImageContentType(contentType, extension),
                Sha256 = hash,
                RecordedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            warnings.Add("The release has no linked discs, so no image evidence record could be created.");
        }

        release.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to save intake {Side} image metadata for release {ReleaseId}", sideName, id);
            warnings.AddRange(await RemoveUnreferencedImageAsync(location, cancellationToken));
            return IntakeAdminResult.Failed(["Failed to save image metadata."], warnings);
        }

        foreach (var previousLocation in previousLocations.Where(previous => !string.Equals(previous, location, StringComparison.Ordinal)))
        {
            warnings.AddRange(await RemoveUnreferencedImageAsync(previousLocation, cancellationToken));
        }

        return IntakeAdminResult.Success($"{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(sideName)} image updated.", warnings);
    }

    public async Task<IntakeAdminResult> DeleteImageAsync(
        int id,
        IntakeImageSide side,
        CancellationToken cancellationToken = default)
    {
        var release = await database.IntakeReleases
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
                    .ThenInclude(disc => disc.Evidence)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (release == null)
        {
            return IntakeAdminResult.Failed("Intake release was not found.");
        }

        var evidence = GetReleaseOwnedEvidence(release)
            .Where(item => item.Type == ToEvidenceType(side))
            .ToList();
        var locations = evidence.Select(item => item.Location).ToHashSet(StringComparer.Ordinal);
        if (side == IntakeImageSide.Front && !string.IsNullOrEmpty(release.FrontImageLocation))
        {
            locations.Add(release.FrontImageLocation);
            release.FrontImageLocation = null;
        }

        if (evidence.Count == 0 && locations.Count == 0)
        {
            return IntakeAdminResult.Success("There is no image to delete.");
        }

        database.IntakeDiscEvidence.RemoveRange(evidence);
        release.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);

        var warnings = new List<string>();
        foreach (var location in locations)
        {
            warnings.AddRange(await RemoveUnreferencedImageAsync(location, cancellationToken));
        }

        string sideName = side == IntakeImageSide.Front ? "Front" : "Back";
        return IntakeAdminResult.Success($"{sideName} image deleted.", warnings);
    }

    public async Task<IntakeDeleteResult> DeleteReleaseAsync(int id, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> failureWarnings = [];
        IntakeDeleteDatabaseResult databaseResult;
        try
        {
            var executionStrategy = database.Database.CreateExecutionStrategy();
            databaseResult = await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = database.Database.IsRelational()
                    ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                    : null;

                var warnings = new List<string>();
                failureWarnings = warnings;
                var release = await LoadReleaseForDeleteAsync(id, cancellationToken);
                if (release == null)
                {
                    return IntakeDeleteDatabaseResult.Failed("Intake release was not found.");
                }

                if (release.Status == IntakeStatus.Promoted || HasCompletedPromotionProvenance(release))
                {
                    return IntakeDeleteDatabaseResult.Failed(
                        "Promoted intake releases and releases with completed promotion provenance cannot be deleted.");
                }

                var discs = release.Discs.Select(link => link.Disc).DistinctBy(item => item.Id).ToList();
                var discIds = discs.Select(disc => disc.Id).ToArray();
                await LockDiscLinksAsync(discIds, cancellationToken);
                var linkedElsewhere = await database.IntakeReleaseDiscs
                    .AsNoTracking()
                    .Where(link => discIds.Contains(link.IntakeDiscId) &&
                        link.IntakeReleaseId != release.Id)
                    .Select(link => link.IntakeDiscId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                var orphanDiscs = discs
                    .Where(disc => !linkedElsewhere.Contains(disc.Id))
                    .ToList();
                var releaseOwnedEvidence = GetReleaseOwnedEvidence(release).ToList();
                var evidenceToDelete = orphanDiscs
                    .SelectMany(disc => disc.Evidence)
                    .Concat(releaseOwnedEvidence)
                    .DistinctBy(item => item.Id)
                    .ToList();
                var cleanup = new List<IntakeAssetCleanup>();

                AddCleanup(cleanup, release.FrontImageLocation, IntakeAssetStore.Image, true);
                foreach (var evidence in evidenceToDelete)
                {
                    AddCleanup(
                        cleanup,
                        evidence.Location,
                        evidence.Type is IntakeDiscEvidenceType.FrontImage or IntakeDiscEvidenceType.BackImage
                            ? IntakeAssetStore.Image
                            : IntakeAssetStore.General,
                        true);
                }

                warnings.AddRange(await FindManifestCleanupAsync(
                    evidenceToDelete.Where(item => item.Type == IntakeDiscEvidenceType.DiscMetadata),
                    cleanup,
                    cancellationToken));

                database.IntakeDiscEvidence.RemoveRange(evidenceToDelete);
                database.IntakePromotions.RemoveRange(release.Promotions);
                database.IntakePromotions.RemoveRange(orphanDiscs.SelectMany(disc => disc.Promotions));
                database.IntakeReleaseDiscs.RemoveRange(release.Discs);
                database.IntakeDiscs.RemoveRange(orphanDiscs);
                database.IntakeReleases.Remove(release);
                await database.SaveChangesAsync(cancellationToken);

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return IntakeDeleteDatabaseResult.Success(
                    discs.Count,
                    orphanDiscs.Count,
                    cleanup,
                    warnings);
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to delete intake release {ReleaseId}", id);
            return IntakeDeleteResult.Failed("Failed to delete the intake release.", failureWarnings);
        }

        if (databaseResult.Failure != null)
        {
            return databaseResult.Failure;
        }

        var cleanupWarnings = await CleanUpAssetsAsync(databaseResult.Cleanup, cancellationToken);
        return IntakeDeleteResult.Success(
            databaseResult.DiscCount,
            databaseResult.OrphanDiscCount,
            [.. databaseResult.Warnings, .. cleanupWarnings]);
    }

    private async Task<IntakeRelease?> LoadReleaseForDeleteAsync(int id, CancellationToken cancellationToken)
    {
        IQueryable<IntakeRelease> query = database.Database.IsSqlServer()
            ? database.IntakeReleases.FromSqlInterpolated(
                $"SELECT * FROM [IntakeReleases] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            : database.IntakeReleases.Where(item => item.Id == id);

        return await query
            .AsSplitQuery()
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
                    .ThenInclude(disc => disc.Releases)
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
                    .ThenInclude(disc => disc.Evidence)
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
                    .ThenInclude(disc => disc.Promotions)
            .Include(item => item.Promotions)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task LockDiscLinksAsync(IEnumerable<int> discIds, CancellationToken cancellationToken)
    {
        if (!database.Database.IsSqlServer())
        {
            return;
        }

        foreach (var discId in discIds)
        {
            await database.IntakeReleaseDiscs
                .FromSqlInterpolated(
                    $"SELECT * FROM [IntakeReleaseDiscs] WITH (UPDLOCK, HOLDLOCK) WHERE [IntakeDiscId] = {discId}")
                .LoadAsync(cancellationToken);
        }
    }

    private async Task<IntakeRelease?> LoadReleaseForDiscUpdateAsync(int id, CancellationToken cancellationToken)
    {
        IQueryable<IntakeRelease> query = database.Database.IsSqlServer()
            ? database.IntakeReleases.FromSqlInterpolated(
                $"SELECT * FROM [IntakeReleases] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            : database.IntakeReleases.Where(item => item.Id == id);

        return await query
            .Include(item => item.Discs)
                .ThenInclude(link => link.Disc)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task LockCanonicalDiscsAndLinksAsync(IEnumerable<int> discIds, CancellationToken cancellationToken)
    {
        if (!database.Database.IsSqlServer())
        {
            return;
        }

        foreach (var discId in discIds)
        {
            await database.IntakeDiscs
                .FromSqlInterpolated(
                    $"SELECT * FROM [IntakeDiscs] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {discId}")
                .LoadAsync(cancellationToken);
            await database.IntakeReleaseDiscs
                .FromSqlInterpolated(
                    $"SELECT * FROM [IntakeReleaseDiscs] WITH (UPDLOCK, HOLDLOCK) WHERE [IntakeDiscId] = {discId}")
                .LoadAsync(cancellationToken);
        }
    }

    private static bool HasCompletedPromotionProvenance(IntakeRelease release) =>
        release.Promotions.Any(promotion => promotion.Status == IntakePromotionStatus.Completed) ||
        release.Discs.Any(link => link.Disc.Promotions.Any(promotion => promotion.Status == IntakePromotionStatus.Completed));

    private static IntakeReleaseDetails MapDetails(IntakeRelease release)
    {
        var releaseEvidence = GetReleaseOwnedEvidence(release).ToList();
        return new IntakeReleaseDetails(
            release.Id,
            release.Source,
            release.SourceReleaseId,
            release.Status,
            release.ReceivedAt,
            release.UpdatedAt,
            release.MediaItemSlug,
            release.BoxsetSlug,
            release.ReleaseSlug,
            release.ExternalProvider,
            release.ExternalId,
            release.Upc,
            release.Asin,
            release.ReleaseDate,
            release.ReleaseTitle,
            release.Locale,
            release.RegionCode,
            release.FrontImageLocation,
            release.Discs
                .OrderBy(link => link.Index ?? int.MaxValue)
                .ThenBy(link => link.Id)
                .Select(link => new IntakeDiscLinkDetails(
                    link.Id,
                    link.Index,
                    link.Name,
                    link.Slug,
                    link.SourceDiscId,
                    link.GlobalDiscId,
                    link.AddedAt,
                    link.Disc.Id,
                    link.Disc.Format,
                    link.Disc.ContentHash,
                    link.Disc.Status,
                    link.Disc.ReceivedAt,
                    link.Disc.UpdatedAt,
                    link.Disc.Releases.Count,
                    link.Disc.Evidence
                        .OrderByDescending(evidence => evidence.RecordedAt)
                        .Select(MapEvidence)
                        .ToList(),
                    link.Disc.Promotions
                        .OrderByDescending(promotion => promotion.CreatedAt)
                        .Select(MapPromotion)
                        .ToList()))
                .ToList(),
            releaseEvidence
                .Where(evidence => evidence.Type is IntakeDiscEvidenceType.FrontImage or IntakeDiscEvidenceType.BackImage)
                .OrderByDescending(evidence => evidence.RecordedAt)
                .Select(MapEvidence)
                .ToList(),
            release.Promotions
                .OrderByDescending(promotion => promotion.CreatedAt)
                .Select(MapPromotion)
                .ToList());
    }

    private static IntakeEvidenceDetails MapEvidence(IntakeDiscEvidence evidence) =>
        new(
            evidence.Id,
            evidence.Source,
            evidence.SourceEvidenceId,
            evidence.EvidenceSetId,
            evidence.GlobalDiscId,
            evidence.Type,
            evidence.Location,
            evidence.ContentType,
            evidence.Sha256,
            evidence.RecordedAt);

    private static IntakePromotionDetails MapPromotion(IntakePromotion promotion) =>
        new(
            promotion.Target,
            promotion.Status,
            promotion.Source,
            promotion.CreatedAt,
            promotion.CompletedAt,
            promotion.FailureReason,
            promotion.MediaItemSlug,
            promotion.BoxsetSlug,
            promotion.ReleaseSlug,
            promotion.DiscFormat,
            promotion.DiscContentHash,
            promotion.DiscGlobalId,
            promotion.EvidenceSetId);

    private static IEnumerable<IntakeDiscEvidence> GetReleaseOwnedEvidence(IntakeRelease release) =>
        release.Discs
            .SelectMany(link => link.Disc.Evidence)
            .Where(evidence =>
                evidence.Source == release.Source &&
                (IsReleaseOwnedEvidenceId(evidence.SourceEvidenceId, release.SourceReleaseId) ||
                 IsReleaseOwnedEvidenceId(evidence.EvidenceSetId, release.SourceReleaseId)))
            .DistinctBy(evidence => evidence.Id);

    private static bool IsReleaseOwnedEvidenceId(string evidenceId, string sourceReleaseId)
    {
        if (string.Equals(evidenceId, sourceReleaseId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!evidenceId.StartsWith(sourceReleaseId, StringComparison.Ordinal) ||
            evidenceId.Length == sourceReleaseId.Length)
        {
            return false;
        }

        return evidenceId[sourceReleaseId.Length] is ':' or '|' or '/' or '\\' or '#';
    }

    private static List<string> ValidateRelease(IntakeReleaseEditRequest request)
    {
        var errors = ValidateDataAnnotations(request);
        string provider = IntakeMatchNormalizer.ExternalProvider(request.ExternalProvider);
        string externalId = IntakeMatchNormalizer.ExternalId(request.ExternalId);

        if (provider == "TMDB")
        {
            if (!long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId) || tmdbId <= 0)
            {
                errors.Add("TMDB ID must be a positive number.");
            }
        }
        else if (provider != "NONE" || externalId != "0")
        {
            errors.Add("External identity must be TMDB with a positive ID, or NONE with ID 0.");
        }

        if (!string.IsNullOrWhiteSpace(request.MediaItemSlug) && !string.IsNullOrWhiteSpace(request.BoxsetSlug))
        {
            errors.Add("Choose either a media item slug or a box set slug, not both.");
        }

        return errors;
    }

    private static List<string> ValidateDiscLinks(
        IntakeRelease release,
        IReadOnlyList<IntakeDiscLinkEditRequest> request)
    {
        var errors = new List<string>();
        if (request.Count != release.Discs.Count ||
            request.Select(item => item.LinkId).Distinct().Count() != request.Count ||
            request.Any(item => release.Discs.All(link => link.Id != item.LinkId)))
        {
            errors.Add("The submitted disc list does not match this release.");
            return errors;
        }

        var duplicateIndexes = request
            .Where(item => item.Index.HasValue)
            .GroupBy(item => item.Index)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateIndexes.Count != 0)
        {
            errors.Add("Each disc index must be unique within the release.");
        }

        foreach (var item in request)
        {
            errors.AddRange(ValidateDataAnnotations(item));
            string format = IntakeMatchNormalizer.Format(item.Format);
            if (!DiscFormatConstants.ContributionFormats.Contains(format, StringComparer.Ordinal))
            {
                errors.Add($"'{item.Format}' is not a supported disc format.");
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<string> ValidateDataAnnotations(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results.Select(result => result.ErrorMessage ?? "Invalid value.").ToList();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ValidateImage(string fileName, string contentType)
    {
        string extension = System.IO.Path.GetExtension(fileName);
        if (!ImageExtensions.Contains(extension) || !ImageContentTypes.Contains(contentType))
        {
            return "Only JPG, PNG, and WebP images are accepted.";
        }

        return null;
    }

    private static string GetImageExtension(string fileName, string contentType)
    {
        string extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".jpeg")
        {
            return ".jpg";
        }

        return extension;
    }

    private static string NormalizeImageContentType(string contentType, string extension) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            _ when extension == ".png" => "image/png",
            _ when extension == ".webp" => "image/webp",
            _ => "image/jpeg",
        };

    private static async Task<byte[]> ReadImageAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            int read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaxImageBytes)
            {
                throw new InvalidOperationException("Image files must be 10 MB or smaller.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (output.Length == 0)
        {
            throw new InvalidOperationException("The image file is empty.");
        }

        return output.ToArray();
    }

    private static IntakeDiscEvidenceType ToEvidenceType(IntakeImageSide side) =>
        side == IntakeImageSide.Front ? IntakeDiscEvidenceType.FrontImage : IntakeDiscEvidenceType.BackImage;

    private async Task<List<string>> RemoveUnreferencedImageAsync(string location, CancellationToken cancellationToken)
    {
        return await DeleteUnreferencedAssetAsync(imageStore, location, "superseded image", cancellationToken);
    }

    private async Task<List<string>> FindManifestCleanupAsync(
        IEnumerable<IntakeDiscEvidence> metadata,
        List<IntakeAssetCleanup> cleanup,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        foreach (var evidence in metadata.Where(item => !string.IsNullOrWhiteSpace(item.Location)))
        {
            try
            {
                var content = await assetStore.Download(evidence.Location, cancellationToken);
                var manifests = ExtractManifestLocations(content);
                if (manifests.Warning != null)
                {
                    logger.LogWarning(
                        "Could not parse intake metadata {Location} for manifest cleanup: {Warning}",
                        evidence.Location,
                        manifests.Warning);
                    warnings.Add($"Could not parse metadata '{evidence.Location}' for manifest cleanup: {manifests.Warning}");
                }

                foreach (var location in manifests.Locations)
                {
                    AddCleanup(cleanup, location, IntakeAssetStore.General, false);
                }
            }
            catch (Exception exception)
            {
                string warning = $"Could not inspect metadata '{evidence.Location}' for manifest cleanup: {exception.Message}";
                logger.LogWarning(exception, "Could not inspect intake metadata {Location} for cleanup", evidence.Location);
                warnings.Add(warning);
            }
        }

        return warnings;
    }

    private static ManifestLocations ExtractManifestLocations(BinaryData data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            return new ManifestLocations(FindManifestLocations(document.RootElement).ToArray(), null);
        }
        catch (JsonException exception)
        {
            return new ManifestLocations([], exception.Message);
        }
    }

    private static IEnumerable<string> FindManifestLocations(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    property.Name.Contains("manifest", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.GetString() is { } location &&
                    location.StartsWith("intake/", StringComparison.OrdinalIgnoreCase))
                {
                    yield return location;
                }

                foreach (var nested in FindManifestLocations(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in FindManifestLocations(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private async Task<List<string>> CleanUpAssetsAsync(
        IEnumerable<IntakeAssetCleanup> cleanup,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        foreach (var item in cleanup.DistinctBy(value => (value.Store, value.Location)))
        {
            var store = item.Store == IntakeAssetStore.Image ? imageStore : assetStore;
            if (item.CheckReferences)
            {
                warnings.AddRange(await DeleteUnreferencedAssetAsync(store, item.Location, "deleted intake asset", cancellationToken));
            }
            else
            {
                warnings.AddRange(await DeleteBlobAsync(store, item.Location, "deleted intake asset", cancellationToken));
            }
        }

        return warnings;
    }

    private async Task<List<string>> DeleteUnreferencedAssetAsync(
        IStaticAssetStore store,
        string location,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            var executionStrategy = database.Database.CreateExecutionStrategy();
            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = database.Database.IsRelational()
                    ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                    : null;

                await LockAssetReferencePredicatesAsync(location, cancellationToken);
                if (await IsAssetReferencedAsync(location, cancellationToken))
                {
                    string warning = $"Skipped cleanup of {description} '{location}' because it is still database referenced.";
                    logger.LogInformation(
                        "Skipped cleanup of {Description} at {Location} because it is still database referenced",
                        description,
                        location);
                    if (transaction != null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return [warning];
                }

                var warnings = await DeleteBlobAsync(store, location, description, cancellationToken);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return warnings;
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to coordinate cleanup of {Description} at {Location}", description, location);
            return [$"Database changes succeeded, but cleanup coordination for {description} '{location}' failed: {exception.Message}"];
        }
    }

    private async Task<bool> IsAssetReferencedAsync(string location, CancellationToken cancellationToken) =>
        await database.IntakeReleases.AsNoTracking()
            .AnyAsync(release => release.FrontImageLocation == location, cancellationToken) ||
        await database.IntakeDiscEvidence.AsNoTracking()
            .AnyAsync(evidence => evidence.Location == location, cancellationToken);

    private async Task LockAssetReferencePredicatesAsync(string location, CancellationToken cancellationToken)
    {
        if (!database.Database.IsSqlServer())
        {
            return;
        }

        await database.IntakeReleases
            .FromSqlInterpolated(
                $"SELECT * FROM [IntakeReleases] WITH (UPDLOCK, HOLDLOCK) WHERE [FrontImageLocation] = {location}")
            .LoadAsync(cancellationToken);
        await database.IntakeDiscEvidence
            .FromSqlInterpolated(
                $"SELECT * FROM [IntakeDiscEvidence] WITH (UPDLOCK, HOLDLOCK) WHERE [Location] = {location}")
            .LoadAsync(cancellationToken);
    }

    private async Task<List<string>> DeleteBlobAsync(
        IStaticAssetStore store,
        string location,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.Delete(location, cancellationToken);
            return [];
        }
        catch (Exception exception)
        {
            string warning = $"Database changes succeeded, but cleanup of {description} '{location}' failed: {exception.Message}";
            logger.LogWarning(exception, "Failed to clean up {Description} at {Location}", description, location);
            return [warning];
        }
    }

    private static void AddCleanup(
        ICollection<IntakeAssetCleanup> cleanup,
        string? location,
        IntakeAssetStore store,
        bool checkReferences)
    {
        if (!string.IsNullOrWhiteSpace(location))
        {
            cleanup.Add(new IntakeAssetCleanup(location, store, checkReferences));
        }
    }
}

public sealed record IntakeReleaseSearch(string? Search, IntakeStatus? Status, int Page = 0, int PageSize = 25);

public sealed record IntakeReleasePage(
    IReadOnlyList<IntakeReleaseListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record IntakeReleaseListItem(
    int Id,
    IntakeStatus Status,
    string? ReleaseTitle,
    string? ReleaseSlug,
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ExternalProvider,
    string ExternalId,
    string Upc,
    DateTimeOffset ReceivedAt,
    int DiscCount);

public sealed record IntakeReleaseDetails(
    int Id,
    IntakeSource Source,
    string SourceReleaseId,
    IntakeStatus Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? UpdatedAt,
    string? MediaItemSlug,
    string? BoxsetSlug,
    string? ReleaseSlug,
    string ExternalProvider,
    string ExternalId,
    string Upc,
    string? Asin,
    DateTimeOffset? ReleaseDate,
    string? ReleaseTitle,
    string? Locale,
    string? RegionCode,
    string? FrontImageLocation,
    IReadOnlyList<IntakeDiscLinkDetails> Discs,
    IReadOnlyList<IntakeEvidenceDetails> ReleaseImages,
    IReadOnlyList<IntakePromotionDetails> Promotions);

public sealed record IntakeDiscLinkDetails(
    int LinkId,
    int? Index,
    string? Name,
    string? Slug,
    string? SourceDiscId,
    string? GlobalDiscId,
    DateTimeOffset AddedAt,
    int CanonicalDiscId,
    string Format,
    string ContentHash,
    IntakeStatus Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? UpdatedAt,
    int ReleaseCount,
    IReadOnlyList<IntakeEvidenceDetails> Evidence,
    IReadOnlyList<IntakePromotionDetails> Promotions);

public sealed record IntakeEvidenceDetails(
    long Id,
    IntakeSource Source,
    string SourceEvidenceId,
    string EvidenceSetId,
    string? GlobalDiscId,
    IntakeDiscEvidenceType Type,
    string Location,
    string? ContentType,
    string? Sha256,
    DateTimeOffset RecordedAt);

public sealed record IntakePromotionDetails(
    IntakePromotionTarget Target,
    IntakePromotionStatus Status,
    IntakeSource Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    string? MediaItemSlug,
    string? BoxsetSlug,
    string? ReleaseSlug,
    string? DiscFormat,
    string? DiscContentHash,
    string? DiscGlobalId,
    string? EvidenceSetId);

public sealed class IntakeReleaseEditRequest
{
    public IntakeStatus Status { get; set; }

    [Required]
    [StringLength(32)]
    public string ExternalProvider { get; set; } = "NONE";

    [Required]
    [StringLength(64)]
    public string ExternalId { get; set; } = "0";

    [Required]
    [Upc]
    [StringLength(32)]
    public string Upc { get; set; } = string.Empty;

    [Asin]
    [StringLength(32)]
    public string? Asin { get; set; }

    public DateTimeOffset? ReleaseDate { get; set; }

    [StringLength(300)]
    public string? ReleaseTitle { get; set; }

    [StringLength(200)]
    public string? ReleaseSlug { get; set; }

    [StringLength(200)]
    public string? MediaItemSlug { get; set; }

    [StringLength(200)]
    public string? BoxsetSlug { get; set; }

    [StringLength(32)]
    public string? Locale { get; set; }

    [StringLength(32)]
    public string? RegionCode { get; set; }

    public static IntakeReleaseEditRequest FromDetails(IntakeReleaseDetails details) =>
        new()
        {
            Status = details.Status,
            ExternalProvider = details.ExternalProvider,
            ExternalId = details.ExternalId,
            Upc = details.Upc,
            Asin = details.Asin,
            ReleaseDate = details.ReleaseDate,
            ReleaseTitle = details.ReleaseTitle,
            ReleaseSlug = details.ReleaseSlug,
            MediaItemSlug = details.MediaItemSlug,
            BoxsetSlug = details.BoxsetSlug,
            Locale = details.Locale,
            RegionCode = details.RegionCode,
        };
}

public sealed class IntakeDiscLinkEditRequest
{
    public int LinkId { get; set; }

    [Range(0, int.MaxValue)]
    public int? Index { get; set; }

    [StringLength(300)]
    public string? Name { get; set; }

    [StringLength(200)]
    public string? Slug { get; set; }

    [Required]
    [StringLength(64)]
    public string Format { get; set; } = DiscFormatConstants.BluRay;

    public static IntakeDiscLinkEditRequest FromDetails(IntakeDiscLinkDetails details) =>
        new()
        {
            LinkId = details.LinkId,
            Index = details.Index,
            Name = details.Name,
            Slug = details.Slug,
            Format = details.Format,
        };
}

public enum IntakeImageSide
{
    Front,
    Back,
}

public sealed record IntakeAdminResult(
    bool Succeeded,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string? Message)
{
    public static IntakeAdminResult Success(string message, IReadOnlyList<string>? warnings = null) =>
        new(true, [], warnings ?? [], message);

    public static IntakeAdminResult Failed(string error, IReadOnlyList<string>? warnings = null) =>
        new(false, [error], warnings ?? [], null);

    public static IntakeAdminResult Failed(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null) =>
        new(false, errors, warnings ?? [], null);
}

public sealed record IntakeDeleteResult(
    bool Succeeded,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int DiscCount,
    int OrphanDiscCount)
{
    public static IntakeDeleteResult Success(int discCount, int orphanDiscCount, IReadOnlyList<string> warnings) =>
        new(true, [], warnings, discCount, orphanDiscCount);

    public static IntakeDeleteResult Failed(string error, IReadOnlyList<string>? warnings = null) =>
        new(false, [error], warnings ?? [], 0, 0);
}

internal enum IntakeAssetStore
{
    General,
    Image,
}

internal sealed record IntakeAssetCleanup(string Location, IntakeAssetStore Store, bool CheckReferences);

internal sealed record IntakeDeleteDatabaseResult(
    IntakeDeleteResult? Failure,
    int DiscCount,
    int OrphanDiscCount,
    IReadOnlyList<IntakeAssetCleanup> Cleanup,
    IReadOnlyList<string> Warnings)
{
    public static IntakeDeleteDatabaseResult Success(
        int discCount,
        int orphanDiscCount,
        IReadOnlyList<IntakeAssetCleanup> cleanup,
        IReadOnlyList<string> warnings) =>
        new(null, discCount, orphanDiscCount, cleanup, warnings);

    public static IntakeDeleteDatabaseResult Failed(string error) =>
        new(IntakeDeleteResult.Failed(error), 0, 0, [], []);
}

internal sealed record ManifestLocations(IReadOnlyList<string> Locations, string? Warning);
