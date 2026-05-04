using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web;

public partial class EngramEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private const long MaxImageSize = 20 * 1024 * 1024; // 20 MB

    [GeneratedRegex(@"^[a-fA-F0-9]{8,128}$")]
    private static partial Regex ContentHashPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]{1,128}$")]
    private static partial Regex ReleaseIdPattern();

    public void MapEndpoints(WebApplication app)
    {
        // TODO: Add API key authentication
        var engram = app.MapGroup("/api/engram");

        engram.MapPost("disc", SubmitDisc);
        engram.MapPost("disc/{contentHash}/logs/scan", UploadScanLog)
            .Accepts<string>("text/plain");
        engram.MapPost("release/{releaseId}/images/front", UploadFrontImage)
            .DisableAntiforgery();
        engram.MapPost("release/{releaseId}/images/back", UploadBackImage)
            .DisableAntiforgery();
    }

    private static IResult? ValidateContentHash(string contentHash)
    {
        if (!ContentHashPattern().IsMatch(contentHash))
        {
            return TypedResults.BadRequest("Invalid content_hash format — must be 8-128 hex characters");
        }
        return null;
    }

    private static IResult? ValidateReleaseId(string releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || !ReleaseIdPattern().IsMatch(releaseId))
        {
            return TypedResults.BadRequest("Invalid release_id format — must be 1-128 characters of letters, digits, underscore, or hyphen");
        }
        return null;
    }

    public async Task<IResult> SubmitDisc(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        EngramExportPayload? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<EngramExportPayload>(
                request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest("Invalid JSON payload");
        }

        if (payload?.Disc == null)
        {
            return TypedResults.BadRequest("Missing required 'disc' section");
        }

        if (string.IsNullOrWhiteSpace(payload.Disc.ContentHash))
        {
            return TypedResults.BadRequest("Missing required 'disc.content_hash'");
        }

        payload.Disc.ContentHash = payload.Disc.ContentHash.Trim().ToUpperInvariant();
        var hashError = ValidateContentHash(payload.Disc.ContentHash);
        if (hashError != null) return hashError;

        if (!string.IsNullOrWhiteSpace(payload.Disc.ReleaseId))
        {
            var releaseError = ValidateReleaseId(payload.Disc.ReleaseId);
            if (releaseError != null) return releaseError;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.EngramDiscs
            .Include(s => s.Titles)
            .FirstOrDefaultAsync(s => s.ContentHash == payload.Disc.ContentHash, cancellationToken);

        var release = await EnsureReleaseAsync(dbContext, payload.Disc.ReleaseId, cancellationToken);

        if (existing != null)
        {
            MapPayloadToDisc(payload, existing, release);
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            existing.Titles.Clear();
            MapTitles(payload, existing);

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { id = existing.Id, contentHash = existing.ContentHash, updated = true });
        }
        else
        {
            var disc = new EngramDisc
            {
                ReceivedAt = DateTimeOffset.UtcNow
            };

            MapPayloadToDisc(payload, disc, release);
            MapTitles(payload, disc);

            dbContext.EngramDiscs.Add(disc);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Concurrent insert lost the race. Could be either:
                // (a) another request inserted the same ContentHash → upsert the existing disc, OR
                // (b) another request inserted the same EngramRelease.ReleaseId → re-use it and insert the disc.
                // We resolve by re-querying both in a fresh context.
                await using var retryContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var concurrent = await retryContext.EngramDiscs
                    .Include(s => s.Titles)
                    .FirstOrDefaultAsync(s => s.ContentHash == payload.Disc.ContentHash, cancellationToken);

                var retryRelease = await EnsureReleaseAsync(retryContext, payload.Disc.ReleaseId, cancellationToken);

                if (concurrent != null)
                {
                    MapPayloadToDisc(payload, concurrent, retryRelease);
                    concurrent.UpdatedAt = DateTimeOffset.UtcNow;
                    concurrent.Titles.Clear();
                    MapTitles(payload, concurrent);

                    await retryContext.SaveChangesAsync(cancellationToken);
                    return Results.Ok(new { id = concurrent.Id, contentHash = concurrent.ContentHash, updated = true });
                }

                var retryDisc = new EngramDisc
                {
                    ReceivedAt = DateTimeOffset.UtcNow
                };
                MapPayloadToDisc(payload, retryDisc, retryRelease);
                MapTitles(payload, retryDisc);
                retryContext.EngramDiscs.Add(retryDisc);

                await retryContext.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { id = retryDisc.Id, contentHash = retryDisc.ContentHash, updated = false });
            }

            return Results.Ok(new { id = disc.Id, contentHash = disc.ContentHash, updated = false });
        }
    }

    public async Task<IResult> UploadScanLog(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IStaticAssetStore assetStore,
        HttpRequest request,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var hashError = ValidateContentHash(contentHash);
        if (hashError != null) return hashError;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var disc = await dbContext.EngramDiscs
            .FirstOrDefaultAsync(s => s.ContentHash == contentHash, cancellationToken);

        if (disc == null)
        {
            return TypedResults.NotFound($"No disc found for content hash '{contentHash}'");
        }

        var blobPath = $"engram/{contentHash}/scan.log";
        await assetStore.Delete(blobPath, cancellationToken);
        await assetStore.Save(request.Body, blobPath, "text/plain", cancellationToken);

        disc.ScanLogPath = blobPath;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { path = blobPath });
    }

    public async Task<IResult> UploadFrontImage(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore assetStore,
        IFormFileCollection files,
        string releaseId,
        CancellationToken cancellationToken)
    {
        return await UploadImage(dbContextFactory, assetStore, files, releaseId, "front", cancellationToken);
    }

    public async Task<IResult> UploadBackImage(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore assetStore,
        IFormFileCollection files,
        string releaseId,
        CancellationToken cancellationToken)
    {
        return await UploadImage(dbContextFactory, assetStore, files, releaseId, "back", cancellationToken);
    }

    private static async Task<IResult> UploadImage(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IStaticAssetStore assetStore,
        IFormFileCollection files,
        string releaseId,
        string side,
        CancellationToken cancellationToken)
    {
        var releaseError = ValidateReleaseId(releaseId);
        if (releaseError != null) return releaseError;

        var file = files.FirstOrDefault();
        if (file == null || file.Length == 0)
        {
            return TypedResults.BadRequest("No file uploaded");
        }

        if (file.Length > MaxImageSize)
        {
            return TypedResults.BadRequest($"File exceeds maximum size of {MaxImageSize / (1024 * 1024)} MB");
        }

        if (!AllowedImageContentTypes.Contains(file.ContentType))
        {
            return TypedResults.BadRequest("Only JPEG, PNG, and WebP images are accepted");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hasDisc = await dbContext.EngramDiscs
            .AnyAsync(s => s.EngramRelease!.ReleaseId == releaseId, cancellationToken);

        if (!hasDisc)
        {
            return TypedResults.NotFound($"No disc found for release_id '{releaseId}'");
        }

        var blobPath = $"engram/release/{releaseId}/{side}.jpg";
        await assetStore.Delete(blobPath, cancellationToken);
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        stream.Position = 0;
        await assetStore.Save(stream, blobPath, file.ContentType, cancellationToken);

        var release = await dbContext.EngramReleases
            .FirstOrDefaultAsync(r => r.ReleaseId == releaseId, cancellationToken);

        if (release == null)
        {
            release = new EngramRelease
            {
                ReleaseId = releaseId,
                ReceivedAt = DateTimeOffset.UtcNow
            };
            dbContext.EngramReleases.Add(release);
        }
        else
        {
            release.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (side == "front")
            release.FrontImageUrl = blobPath;
        else
            release.BackImageUrl = blobPath;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { path = blobPath });
    }

    private static async Task<EngramRelease?> EnsureReleaseAsync(SqlServerDataContext dbContext, string? releaseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
        {
            return null;
        }

        var release = await dbContext.EngramReleases
            .FirstOrDefaultAsync(r => r.ReleaseId == releaseId, cancellationToken);

        if (release == null)
        {
            release = new EngramRelease
            {
                ReleaseId = releaseId,
                ReceivedAt = DateTimeOffset.UtcNow
            };
            dbContext.EngramReleases.Add(release);
        }

        return release;
    }

    private static void MapPayloadToDisc(EngramExportPayload payload, EngramDisc disc, EngramRelease? release)
    {
        disc.ContentHash = payload.Disc!.ContentHash!;
        disc.VolumeLabel = payload.Disc.VolumeLabel ?? string.Empty;
        disc.ContentType = payload.Disc.ContentType ?? string.Empty;
        disc.DiscNumber = payload.Disc.DiscNumber;
        disc.EngramRelease = release;
        disc.EngramVersion = payload.EngramVersion ?? string.Empty;
        disc.ExportVersion = payload.ExportVersion ?? string.Empty;
        disc.ContributionTier = payload.ContributionTier;
        disc.Upc = payload.Upc;

        if (payload.Identification != null)
        {
            disc.TmdbId = payload.Identification.TmdbId;
            disc.DetectedTitle = payload.Identification.DetectedTitle;
            disc.DetectedSeason = payload.Identification.DetectedSeason;
            disc.ClassificationSource = payload.Identification.ClassificationSource;
            disc.ClassificationConfidence = payload.Identification.ClassificationConfidence;
        }
        else
        {
            disc.TmdbId = null;
            disc.DetectedTitle = null;
            disc.DetectedSeason = null;
            disc.ClassificationSource = null;
            disc.ClassificationConfidence = null;
        }
    }

    private static void MapTitles(EngramExportPayload payload, EngramDisc disc)
    {
        if (payload.Titles == null) return;

        foreach (var t in payload.Titles)
        {
            disc.Titles.Add(new EngramTitle
            {
                TitleIndex = t.Index,
                SourceFilename = t.SourceFilename,
                DurationSeconds = t.DurationSeconds,
                SizeBytes = t.SizeBytes,
                ChapterCount = t.ChapterCount,
                SegmentCount = t.SegmentCount,
                SegmentMap = t.SegmentMap,
                TitleType = t.TitleType,
                Season = t.Season,
                Episode = t.Episode,
                MatchConfidence = t.MatchConfidence,
                MatchSource = t.MatchSource,
                Edition = t.Edition
            });
        }
    }
}

#region Engram JSON DTOs

public class EngramExportPayload
{
    [JsonPropertyName("engram_version")]
    public string? EngramVersion { get; set; }

    [JsonPropertyName("export_version")]
    public string? ExportVersion { get; set; }

    [JsonPropertyName("exported_at")]
    public DateTimeOffset? ExportedAt { get; set; }

    [JsonPropertyName("contribution_tier")]
    public int ContributionTier { get; set; }

    [JsonPropertyName("disc")]
    public EngramDiscDto? Disc { get; set; }

    [JsonPropertyName("identification")]
    public EngramIdentificationDto? Identification { get; set; }

    [JsonPropertyName("titles")]
    public List<EngramTitleDto>? Titles { get; set; }

    [JsonPropertyName("upc")]
    public string? Upc { get; set; }

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }
}

public class EngramDiscDto
{
    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; set; }

    [JsonPropertyName("volume_label")]
    public string? VolumeLabel { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("disc_number")]
    public int? DiscNumber { get; set; }
    [JsonPropertyName("release_id")]
    public string? ReleaseId { get; set; }
}

public class EngramIdentificationDto
{
    [JsonPropertyName("tmdb_id")]
    public int? TmdbId { get; set; }

    [JsonPropertyName("detected_title")]
    public string? DetectedTitle { get; set; }

    [JsonPropertyName("detected_season")]
    public int? DetectedSeason { get; set; }

    [JsonPropertyName("classification_source")]
    public string? ClassificationSource { get; set; }

    [JsonPropertyName("classification_confidence")]
    public double? ClassificationConfidence { get; set; }
}

public class EngramTitleDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("source_filename")]
    public string? SourceFilename { get; set; }

    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; set; }

    [JsonPropertyName("size_bytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("chapter_count")]
    public int? ChapterCount { get; set; }

    [JsonPropertyName("segment_count")]
    public int? SegmentCount { get; set; }

    [JsonPropertyName("segment_map")]
    public string? SegmentMap { get; set; }

    [JsonPropertyName("title_type")]
    public string? TitleType { get; set; }

    [JsonPropertyName("season")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Season { get; set; }

    [JsonPropertyName("episode")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Episode { get; set; }

    [JsonPropertyName("match_confidence")]
    public double? MatchConfidence { get; set; }

    [JsonPropertyName("match_source")]
    public string? MatchSource { get; set; }

    [JsonPropertyName("edition")]
    public string? Edition { get; set; }
}

#endregion

/// <summary>
/// Reads a JSON value as a string whether the token is a string or a number.
/// </summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token type {reader.TokenType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
