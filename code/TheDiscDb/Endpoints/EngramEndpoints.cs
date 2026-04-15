using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
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

    public void MapEndpoints(WebApplication app)
    {
        // TODO: Add API key authentication
        var engram = app.MapGroup("/api/engram");

        engram.MapPost("disc", SubmitDisc);
        engram.MapPost("disc/{contentHash}/logs/scan", UploadScanLog)
            .Accepts<string>("text/plain");
        engram.MapPost("disc/{contentHash}/images/front", UploadFrontImage)
            .DisableAntiforgery();
        engram.MapPost("disc/{contentHash}/images/back", UploadBackImage)
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

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.EngramSubmissions
            .Include(s => s.Titles)
            .FirstOrDefaultAsync(s => s.ContentHash == payload.Disc.ContentHash, cancellationToken);

        if (existing != null)
        {
            MapPayloadToSubmission(payload, existing);
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            existing.Titles.Clear();
            MapTitles(payload, existing);

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { id = existing.Id, contentHash = existing.ContentHash, updated = true });
        }
        else
        {
            var submission = new EngramSubmission
            {
                ReceivedAt = DateTimeOffset.UtcNow
            };

            MapPayloadToSubmission(payload, submission);
            MapTitles(payload, submission);

            dbContext.EngramSubmissions.Add(submission);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Concurrent insert won the race — retry as update
                await using var retryContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var concurrent = await retryContext.EngramSubmissions
                    .Include(s => s.Titles)
                    .FirstAsync(s => s.ContentHash == payload.Disc.ContentHash, cancellationToken);

                MapPayloadToSubmission(payload, concurrent);
                concurrent.UpdatedAt = DateTimeOffset.UtcNow;
                concurrent.Titles.Clear();
                MapTitles(payload, concurrent);

                await retryContext.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { id = concurrent.Id, contentHash = concurrent.ContentHash, updated = true });
            }

            return Results.Ok(new { id = submission.Id, contentHash = submission.ContentHash, updated = false });
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
        var submission = await dbContext.EngramSubmissions
            .FirstOrDefaultAsync(s => s.ContentHash == contentHash, cancellationToken);

        if (submission == null)
        {
            return TypedResults.NotFound($"No submission found for content hash '{contentHash}'");
        }

        var blobPath = $"engram/{contentHash}/scan.log";
        await assetStore.Delete(blobPath, cancellationToken);
        await assetStore.Save(request.Body, blobPath, "text/plain", cancellationToken);

        submission.ScanLogPath = blobPath;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { path = blobPath });
    }

    public async Task<IResult> UploadFrontImage(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore assetStore,
        IFormFileCollection files,
        string contentHash,
        CancellationToken cancellationToken)
    {
        return await UploadImage(dbContextFactory, assetStore, files, contentHash, "front", cancellationToken);
    }

    public async Task<IResult> UploadBackImage(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore assetStore,
        IFormFileCollection files,
        string contentHash,
        CancellationToken cancellationToken)
    {
        return await UploadImage(dbContextFactory, assetStore, files, contentHash, "back", cancellationToken);
    }

    private static async Task<IResult> UploadImage(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IStaticAssetStore assetStore,
        IFormFileCollection files,
        string contentHash,
        string side,
        CancellationToken cancellationToken)
    {
        var hashError = ValidateContentHash(contentHash);
        if (hashError != null) return hashError;

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
        var submission = await dbContext.EngramSubmissions
            .FirstOrDefaultAsync(s => s.ContentHash == contentHash, cancellationToken);

        if (submission == null)
        {
            return TypedResults.NotFound($"No submission found for content hash '{contentHash}'");
        }

        var blobPath = $"engram/{contentHash}/{side}.jpg";
        await assetStore.Delete(blobPath, cancellationToken);
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        stream.Position = 0;
        await assetStore.Save(stream, blobPath, file.ContentType, cancellationToken);

        if (side == "front")
            submission.FrontImageUrl = blobPath;
        else
            submission.BackImageUrl = blobPath;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { path = blobPath });
    }

    private static void MapPayloadToSubmission(EngramExportPayload payload, EngramSubmission submission)
    {
        submission.ContentHash = payload.Disc!.ContentHash!;
        submission.VolumeLabel = payload.Disc.VolumeLabel ?? string.Empty;
        submission.ContentType = payload.Disc.ContentType ?? string.Empty;
        submission.DiscNumber = payload.Disc.DiscNumber;
        submission.ReleaseId = payload.Disc?.ReleaseId;
        submission.EngramVersion = payload.EngramVersion ?? string.Empty;
        submission.ExportVersion = payload.ExportVersion ?? string.Empty;
        submission.ContributionTier = payload.ContributionTier;
        submission.Upc = payload.Upc;

        if (payload.Identification != null)
        {
            submission.TmdbId = payload.Identification.TmdbId;
            submission.DetectedTitle = payload.Identification.DetectedTitle;
            submission.DetectedSeason = payload.Identification.DetectedSeason;
            submission.ClassificationSource = payload.Identification.ClassificationSource;
            submission.ClassificationConfidence = payload.Identification.ClassificationConfidence;
        }
        else
        {
            submission.TmdbId = null;
            submission.DetectedTitle = null;
            submission.DetectedSeason = null;
            submission.ClassificationSource = null;
            submission.ClassificationConfidence = null;
        }
    }

    private static void MapTitles(EngramExportPayload payload, EngramSubmission submission)
    {
        if (payload.Titles == null) return;

        foreach (var t in payload.Titles)
        {
            submission.Titles.Add(new EngramTitle
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
