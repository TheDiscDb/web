using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http.Json;
using FluentResults;
using MakeMkv;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Services.DiscLookup;
using TheDiscDb.Services.Server;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web;

public class ContributionEndpoints
{
    public void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/admin/contribution-discs/{discId:int}/assets/{kind}", DownloadDiscAsset)
            .RequireAuthorization("Admin");

        var contribute = app.MapGroup("/api/contribute").RequireAuthorization();

        contribute.MapPost("{contributionId}/discs/{discId}/logs", SaveDiscLogs)
            .AllowAnonymous()
            .Accepts<string>("text/plain");
        contribute.MapPost("{contributionId}/discs/{discId}/manifest", SaveDiscManifest)
            .AllowAnonymous()
            .Accepts<string>("application/json");
        contribute.MapDelete("{contributionId}/discs/{discId}/logs/error", ClearDiscLogError)
            .AllowAnonymous();
        contribute.MapPost("disc/{discHash}/scan", SavePreContributionDiscScanDraft)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .Accepts<DiscScanDraft>("application/json");
        contribute.MapGet("disc/{discHash}/scan", GetPreContributionDiscScanDraft);
        contribute.MapDelete("disc/{discHash}/scan", DeletePreContributionDiscScanDraft);
        contribute.MapPost("disc/{discHash}/scan/contribution/{contributionId}", LinkPreContributionDiscScanDraft);
        contribute.MapPost("{contributionId}/discs/{discId}/scan/{discHash}/attach", AttachPreContributionDiscScanDraft);
        contribute.MapGet("externalsearch/{type}", ExternalSearch);

        contribute.MapPost("images/front/upload/{id:guid}", UploadFrontImage)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .DisableAntiforgery();
        contribute.MapPost("images/front/remove/{id:guid}", RemoveFrontImage)
            .DisableAntiforgery();
        contribute.MapPost("images/back/upload/{id:guid}", UploadBackImage)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .DisableAntiforgery();
        contribute.MapPost("images/back/remove/{id:guid}", RemoveBackImage)
            .DisableAntiforgery();

        contribute.MapPost("{contributionId}/images/front/upload", UploadContributionFrontImage)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .DisableAntiforgery();
        contribute.MapPost("{contributionId}/images/back/upload", UploadContributionBackImage)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .DisableAntiforgery();
        contribute.MapPost("{contributionId}/images/back/delete", DeleteContributionBackImage)
            .DisableAntiforgery();

        contribute.MapPost("boxset/{boxsetId}/images/front/upload", UploadBoxsetFrontImage)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .DisableAntiforgery();
        contribute.MapPost("boxset/{boxsetId}/images/back/upload", UploadBoxsetBackImage)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .DisableAntiforgery();
        contribute.MapPost("boxset/{boxsetId}/images/front/delete", DeleteBoxsetFrontImage)
            .DisableAntiforgery();
        contribute.MapPost("boxset/{boxsetId}/images/back/delete", DeleteBoxsetBackImage)
            .DisableAntiforgery();

        contribute.MapGet("images/{**path}", ServeContributionImage);
    }

    public async Task<IResult> SavePreContributionDiscScanDraft(
        string discHash,
        HttpRequest request,
        UserManager<TheDiscDbUser> userManager,
        HttpContext httpContext,
        IStaticAssetStore assetStore,
        IWebHostEnvironment environment,
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!DiscScanDraft.TryDecodeHashFromRoute(discHash, out discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash route.");
        }

        if (!environment.IsDevelopment() && !DiscLookupQuery.IsValidDiscHash(discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash — must be a 32-character hex string.");
        }

        string? userId = userManager.GetUserId(httpContext.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.Unauthorized();
        }

        DiscScanDraft? draft = await request.ReadFromJsonAsync<DiscScanDraft>(cancellationToken);
        if (draft is null
            || draft.IsExistingDiscReference != string.IsNullOrWhiteSpace(draft.ManifestJson)
            || draft.HashFiles is null || draft.HashFiles.Count == 0
            || draft.FingerprintFiles is null || draft.FingerprintFiles.Count == 0)
        {
            return TypedResults.BadRequest("The disc scan draft is incomplete.");
        }

        if (draft.HasManualHashOverride && !environment.IsDevelopment())
        {
            return TypedResults.BadRequest("Manual disc hash overrides are only allowed in Development.");
        }

        string normalizedHash = discHash.ToUpperInvariant();
        string calculatedHash;
        string calculatedFingerprint;
        try
        {
            calculatedHash = draft.HashFiles
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .CalculateHash();
            calculatedFingerprint = DiscFingerprint.Calculate(draft.FingerprintFiles);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }

        if (!string.Equals(draft.DiscHash, normalizedHash, StringComparison.OrdinalIgnoreCase)
            || (!draft.HasManualHashOverride
                && !string.Equals(calculatedHash, normalizedHash, StringComparison.OrdinalIgnoreCase))
            || !string.Equals(draft.Fingerprint, calculatedFingerprint, StringComparison.Ordinal))
        {
            return TypedResults.BadRequest("The scan identifiers do not match the selected disc.");
        }

        if (!DiscFormatConstants.ContributionFormats.Contains(draft.Format, StringComparer.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(draft.GlobalDiscId) && !DiscLookupQuery.IsValidDiscId(draft.GlobalDiscId)))
        {
            return TypedResults.BadRequest("The disc scan contains an unsupported format or invalid disc identifier.");
        }

        if (draft.IsExistingDiscReference)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (!await dbContext.Discs.AnyAsync(disc => disc.ContentHash == normalizedHash, cancellationToken))
            {
                return TypedResults.BadRequest("This disc is not in the database yet, so it needs a full scan.");
            }
        }
        else
        {
            IResult? manifestError = ValidateDraftManifest(draft);
            if (manifestError is not null)
            {
                return manifestError;
            }
        }

        string path = GetPreContributionDraftPath(userId, normalizedHash);
        await assetStore.Delete(path, cancellationToken);
        var savedDraft = draft with
        {
            DiscHash = normalizedHash,
            CreatedUtc = DateTimeOffset.UtcNow,
            ContributionId = null,
        };
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(savedDraft));
        await assetStore.Save(stream, path, ContentTypes.JsonContentType, cancellationToken);
        return TypedResults.Ok(new { discHash = normalizedHash.ToLowerInvariant() });
    }

    private static IResult? ValidateDraftManifest(DiscScanDraft draft)
    {
        var parsed = new OpticalDiscManifestValidator().Parse(draft.ManifestJson);
        if (!parsed.IsValid)
        {
            return TypedResults.BadRequest(parsed.Error);
        }

        if (parsed.Document?.Disc?.Format is not ("dvd" or "blu-ray" or "uhd-blu-ray"))
        {
            return TypedResults.BadRequest("The manifest contains an unsupported disc format.");
        }

        if (!string.Equals(draft.Format,
            DiscFormatConstants.FromManifestFormat(parsed.Document.Disc.Format),
            StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest("The disc format does not match the scanned manifest.");
        }

        try
        {
            OpticalDiscManifestMapper.ToDiscInfo(parsed.Document!);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            return TypedResults.BadRequest("The manifest does not contain titles that can be identified.");
        }

        return null;
    }

    public async Task<IResult> GetPreContributionDiscScanDraft(
        string discHash,
        UserManager<TheDiscDbUser> userManager,
        HttpContext httpContext,
        IStaticAssetStore assetStore,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!DiscScanDraft.TryDecodeHashFromRoute(discHash, out discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash route.");
        }

        if (!environment.IsDevelopment() && !DiscLookupQuery.IsValidDiscHash(discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash — must be a 32-character hex string.");
        }

        string? userId = userManager.GetUserId(httpContext.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.Unauthorized();
        }

        string path = GetPreContributionDraftPath(userId, discHash.ToUpperInvariant());
        if (!await assetStore.Exists(path, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        DiscScanDraft? draft = (await assetStore.Download(path, cancellationToken))
            .ToObjectFromJson<DiscScanDraft>();
        if (draft is null || draft.CreatedUtc < DateTimeOffset.UtcNow.AddDays(-30))
        {
            await assetStore.Delete(path, cancellationToken);
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(draft);
    }

    public async Task<IResult> DeletePreContributionDiscScanDraft(
        string discHash,
        UserManager<TheDiscDbUser> userManager,
        HttpContext httpContext,
        IStaticAssetStore assetStore,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!DiscScanDraft.TryDecodeHashFromRoute(discHash, out discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash route.");
        }

        if (!environment.IsDevelopment() && !DiscLookupQuery.IsValidDiscHash(discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash — must be a 32-character hex string.");
        }

        string? userId = userManager.GetUserId(httpContext.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.Unauthorized();
        }

        await assetStore.Delete(GetPreContributionDraftPath(userId, discHash.ToUpperInvariant()), cancellationToken);
        return TypedResults.NoContent();
    }

    public async Task<IResult> LinkPreContributionDiscScanDraft(
        string discHash,
        string contributionId,
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        HttpContext httpContext,
        IStaticAssetStore assetStore,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!DiscScanDraft.TryDecodeHashFromRoute(discHash, out discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash route.");
        }

        if (!environment.IsDevelopment() && !DiscLookupQuery.IsValidDiscHash(discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash — must be a 32-character hex string.");
        }

        string? userId = userManager.GetUserId(httpContext.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.Unauthorized();
        }

        int contributionKey = idEncoder.Decode(contributionId);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var contribution = await dbContext.UserContributions
            .FirstOrDefaultAsync(item => item.Id == contributionKey, cancellationToken);
        if (contribution is null || contribution.UserId != userId)
        {
            return TypedResults.NotFound();
        }

        if (!contribution.Status.IsEditableByOwner())
        {
            return TypedResults.Conflict("This contribution is no longer editable.");
        }

        string path = GetPreContributionDraftPath(userId, discHash.ToUpperInvariant());
        if (!await assetStore.Exists(path, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        DiscScanDraft? draft = (await assetStore.Download(path, cancellationToken))
            .ToObjectFromJson<DiscScanDraft>();
        if (draft is null || !string.Equals(draft.DiscHash, discHash, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Conflict("The saved scan does not match this contribution.");
        }

        var linkedDraft = draft with { ContributionId = contributionId };
        await assetStore.Delete(path, cancellationToken);
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(linkedDraft));
        await assetStore.Save(stream, path, ContentTypes.JsonContentType, cancellationToken);
        return TypedResults.Ok();
    }

    public async Task<IResult> AttachPreContributionDiscScanDraft(
        string contributionId,
        string discId,
        string discHash,
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        HttpContext httpContext,
        IStaticAssetStore assetStore,
        IDiscScanComparisonService comparisonService,
        ILogger<ContributionEndpoints> logger,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!DiscScanDraft.TryDecodeHashFromRoute(discHash, out discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash route.");
        }

        if (!environment.IsDevelopment() && !DiscLookupQuery.IsValidDiscHash(discHash))
        {
            return TypedResults.BadRequest("Invalid disc hash — must be a 32-character hex string.");
        }

        string? userId = userManager.GetUserId(httpContext.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.Unauthorized();
        }

        int contributionKey = idEncoder.Decode(contributionId);
        int discKey = idEncoder.Decode(discId);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var contribution = await dbContext.UserContributions
            .Include(item => item.Discs)
            .FirstOrDefaultAsync(item => item.Id == contributionKey, cancellationToken);
        if (contribution is null || contribution.UserId != userId)
        {
            return TypedResults.NotFound();
        }

        if (!contribution.Status.IsEditableByOwner())
        {
            return TypedResults.Conflict("This contribution is no longer editable.");
        }

        var disc = contribution.Discs.FirstOrDefault(item => item.Id == discKey);
        if (disc is null || !string.Equals(disc.ContentHash, discHash, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.NotFound();
        }

        string draftPath = GetPreContributionDraftPath(userId, discHash.ToUpperInvariant());
        if (!await assetStore.Exists(draftPath, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        DiscScanDraft? draft = (await assetStore.Download(draftPath, cancellationToken))
            .ToObjectFromJson<DiscScanDraft>();
        if (draft is null || !string.Equals(draft.DiscHash, discHash, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Conflict("The saved scan does not match this contribution disc.");
        }

        if (draft.HasManualHashOverride && !environment.IsDevelopment())
        {
            return TypedResults.BadRequest("Manual disc hash overrides are only allowed in Development.");
        }

        if (draft.IsExistingDiscReference)
        {
            return TypedResults.Conflict("This scan references an existing disc and has no manifest to attach.");
        }

        var saved = await SaveDiscManifestInternal(
            dbContextFactory,
            idEncoder,
            assetStore,
            contributionId,
            discId,
            draft.ManifestJson,
            cancellationToken,
            httpContext.Request.Headers.UserAgent.ToString(),
            comparisonService,
            logger);
        if (saved.IsFailed)
        {
            return OkOrProblem(saved, $"Unable to attach the scan for contribution {contributionId}, disc {discId}");
        }

        if (draft.HasManualHashOverride)
        {
            var existingHashItems = await dbContext.UserContributionDiscHashItems
                .Where(item => item.UserContribution.Id == contributionKey && item.DiscHash == draft.DiscHash)
                .ToListAsync(cancellationToken);
            dbContext.UserContributionDiscHashItems.RemoveRange(existingHashItems);
            foreach (var file in draft.HashFiles)
            {
                dbContext.UserContributionDiscHashItems.Add(new UserContributionDiscHashItem
                {
                    UserContribution = contribution,
                    DiscHash = draft.DiscHash,
                    Index = file.Index,
                    Name = file.Name ?? string.Empty,
                    Size = file.Size,
                    CreationTime = file.CreationTime,
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await assetStore.Delete(draftPath, cancellationToken);
        return TypedResults.Ok();
    }

    private static string GetPreContributionDraftPath(string userId, string discHash)
    {
        string ownerKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant();
        string storageKey = DiscLookupQuery.IsValidDiscHash(discHash)
            ? discHash
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(discHash)));
        return ContributionDiscAssets.PreContributionDraftPath(ownerKey, storageKey);
    }

    private static async Task<IResult> DownloadDiscAsset(
        int discId,
        string kind,
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IStaticAssetStore assetStore,
        IdEncoder idEncoder,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var disc = await dbContext.UserContributionDiscs
            .AsNoTracking()
            .Include(item => item.UserContribution)
            .FirstOrDefaultAsync(item => item.Id == discId, cancellationToken);

        if (disc is null)
        {
            return Results.NotFound();
        }

        string contributionId = idEncoder.Encode(disc.UserContribution.Id);
        string encodedDiscId = idEncoder.Encode(disc.Id);
        string discLabel = $"disc{disc.Index ?? disc.Id:00}";
        DiscAssetDownload? asset = kind.ToLowerInvariant() switch
        {
            "manifest" => new(ContributionDiscAssets.ManifestPath(contributionId, encodedDiscId), $"{discLabel}.odm.json", ContentTypes.JsonContentType),
            "log" => new(ContributionDiscAssets.LogsPath(contributionId, encodedDiscId), $"{discLabel}.txt", ContentTypes.TextContentType),
            "comparison" => new(ContributionDiscAssets.ComparisonPath(contributionId, encodedDiscId), $"{discLabel}-comparison.json", ContentTypes.JsonContentType),
            _ => null
        };

        if (asset is null || !await assetStore.Exists(asset.RemotePath, cancellationToken))
        {
            return Results.NotFound();
        }

        var data = await assetStore.Download(asset.RemotePath, cancellationToken);
        return Results.File(data.ToArray(), asset.ContentType, asset.FileName);
    }

    private sealed record DiscAssetDownload(string RemotePath, string FileName, string ContentType);

    public async Task<IResult> ExternalSearch(IExternalSearchService service, string type, [FromQuery] string query, CancellationToken cancellationToken)
    {
        if (type.Equals("movie", StringComparison.OrdinalIgnoreCase))
        {
            var response = await service.SearchMovies(query, cancellationToken);
            return JsonResult<ExternalSearchResponse>(response, $"Unable to search for movies with query {query}");
        }
        else if (type.Equals("series", StringComparison.OrdinalIgnoreCase) || type.Equals("tv", StringComparison.OrdinalIgnoreCase))
        {
            var results = await service.SearchSeries(query, cancellationToken);
            return JsonResult(results, $"Unable to search for series with query {query}");
        }

        return TypedResults.BadRequest($"Unknown external search type {type}");
    }

    public async Task<IResult> SaveDiscLogs(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IdEncoder idEncoder,
        IStaticAssetStore assetStore,
        IDiscScanComparisonService comparisonService,
        ILogger<ContributionEndpoints> logger,
        HttpRequest request,
        string contributionId,
        string discId,
        CancellationToken cancellationToken)
    {
        using (StreamReader reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            string logs = await reader.ReadToEndAsync();
            var result = await SaveDiscLogsInternal(dbContextFactory, idEncoder, assetStore, contributionId, discId, logs, cancellationToken, comparisonService, logger);
            // TODO: include a traceid people can share to look up problem later
            return OkOrProblem(result, $"Unable to save disc logs for contribution {contributionId}, disc {discId}");
        }
    }

    private IResult OkOrProblem(FluentResults.Result result, string problemMessage)
    {
        if (result.IsSuccess)
        {
            return Results.Ok();
        }
        else
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(problemMessage);
            sb.AppendLine("Errors:");
            foreach (var error in result.Errors)
            {
                sb.AppendLine($"- {error.Message}");
            }

            return TypedResults.Problem(sb.ToString());
        }
    }

    private IResult JsonResult<T>(FluentResults.Result<T> result, string problemMessage)
    {
        if (result.IsSuccess)
        {
            return Results.Json(result.Value);
        }
        else
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(problemMessage);
            sb.AppendLine("Errors:");
            foreach (var error in result.Errors)
            {
                sb.AppendLine($"- {error.Message}");
            }

            return TypedResults.Problem(sb.ToString());
        }
    }

    public async Task<Result> SaveDiscLogsInternal(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IdEncoder idEncoder,
        IStaticAssetStore assetStore,
        string contributionId,
        string discId,
        string logs,
        CancellationToken cancellationToken,
        IDiscScanComparisonService? comparisonService = null,
        ILogger<ContributionEndpoints>? logger = null)
    {
        int savedDiscId;
        bool manifestExists;
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            int id = idEncoder.Decode(contributionId);
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (contribution == null)
            {
                return Result.Fail($"Contribution {contributionId} not found");
            }

            int realDiscId = idEncoder.Decode(discId);
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return Result.Fail($"Disc {discId} not found");
            }

            // Convert any LF line endings to CRLF
            logs = logs.Replace("\r\n", "\n") // normalize any CRLF to LF first
                .Replace("\n", "\r\n"); // then convert LF to CRLF

            byte[] byteArray = Encoding.UTF8.GetBytes(logs);
            using (MemoryStream memoryStream = new MemoryStream(byteArray))
            {
                // Validate the logs are from makemkv and not something else
                memoryStream.Position = 0;
                List<string> allLines = new();
                using (StreamReader reader = new StreamReader(memoryStream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        allLines.Add(line);
                    }

                    try
                    {
                        var parsed = LogParser.Parse(allLines).ToList();
                        if (parsed.Count == 0)
                        {
                            disc.LogsUploaded = false;
                            disc.LogUploadError = "Log file contains no valid MakeMKV log entries";
                            await dbContext.SaveChangesAsync(cancellationToken);
                            return Result.Fail(disc.LogUploadError);
                        }

                        LogParser.Organize(parsed);
                    }
                    catch (Exception)
                    {
                        disc.LogsUploaded = false;
                        disc.LogUploadError = "Could not parse log file";
                        await dbContext.SaveChangesAsync(cancellationToken);
                        return Result.Fail(disc.LogUploadError);
                    }
                }

                // TODO: if the logs have changed, rewrite the memorystream

                //Save the logs in blob storage
                memoryStream.Position = 0;
                await assetStore.Save(memoryStream, $"{contributionId}/{idEncoder.Encode(disc.Id)}-logs.txt", ContentTypes.TextContentType, cancellationToken);
            }

            disc.LogsUploaded = true;
            disc.LogUploadError = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            savedDiscId = disc.Id;
            manifestExists = await assetStore.Exists(ContributionDiscAssets.ManifestPath(contributionId, idEncoder.Encode(disc.Id)), cancellationToken);
        }

        if (manifestExists)
        {
            await TryCompareDiscAsync(comparisonService, logger, savedDiscId, cancellationToken);
        }

        return Result.Ok();

        //TODO: Notify the client a disc has been added? (to prevent the client having to poll)
    }

    public async Task<IResult> SaveDiscManifest(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IdEncoder idEncoder,
        IStaticAssetStore assetStore,
        IDiscScanComparisonService comparisonService,
        ILogger<ContributionEndpoints> logger,
        HttpRequest request,
        string contributionId,
        string discId,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        string manifest = await reader.ReadToEndAsync(cancellationToken);
        var result = await SaveDiscManifestInternal(
            dbContextFactory,
            idEncoder,
            assetStore,
            contributionId,
            discId,
            manifest,
            cancellationToken,
            request.Headers["User-Agent"].ToString(),
            comparisonService,
            logger);
        return OkOrProblem(result, $"Unable to save the disc manifest for contribution {contributionId}, disc {discId}");
    }

    public async Task<Result> SaveDiscManifestInternal(
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IdEncoder idEncoder,
        IStaticAssetStore assetStore,
        string contributionId,
        string discId,
        string manifest,
        CancellationToken cancellationToken,
        string? userAgent = null,
        IDiscScanComparisonService? comparisonService = null,
        ILogger<ContributionEndpoints>? logger = null)
    {
        int savedDiscId;
        bool logExists;
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        int id = idEncoder.Decode(contributionId);
        var contribution = await dbContext.UserContributions
            .Include(c => c.Discs)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (contribution == null)
        {
            return Result.Fail($"Contribution {contributionId} not found");
        }

        int realDiscId = idEncoder.Decode(discId);
        var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

        if (disc == null)
        {
            return Result.Fail($"Disc {discId} not found");
        }

        logExists = await assetStore.Exists(ContributionDiscAssets.LogsPath(contributionId, idEncoder.Encode(disc.Id)), cancellationToken);

        var parsed = new OpticalDiscManifestValidator().Parse(manifest);
        if (!parsed.IsValid)
        {
            if (!logExists)
            {
                disc.LogsUploaded = false;
                disc.LogUploadError = parsed.Error;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Result.Fail(parsed.Error!);
        }

        // Mapping now means a manifest that the identify flow cannot use is rejected at upload
        // time rather than failing later, when the contributor has moved on.
        try
        {
            OpticalDiscManifestMapper.ToDiscInfo(parsed.Document!);
        }
        catch (Exception)
        {
            const string error = "The manifest is valid but does not describe any titles that can be identified.";
            if (!logExists)
            {
                disc.LogsUploaded = false;
                disc.LogUploadError = error;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Result.Fail(error);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(manifest);
        string manifestPath = ContributionDiscAssets.ManifestPath(contributionId, idEncoder.Encode(disc.Id));
        await assetStore.Delete(manifestPath, cancellationToken);
        using (var memoryStream = new MemoryStream(bytes))
        {
            await assetStore.Save(memoryStream, manifestPath, ContentTypes.JsonContentType, cancellationToken);
        }

        disc.ManifestUploaded = true;
        disc.ManifestUserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
        if (!logExists)
        {
            disc.LogsUploaded = true;
            disc.LogUploadError = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        savedDiscId = disc.Id;

        if (logExists)
        {
            await TryCompareDiscAsync(comparisonService, logger, savedDiscId, cancellationToken);
        }

        return Result.Ok();
    }

    private static async Task TryCompareDiscAsync(
        IDiscScanComparisonService? comparisonService,
        ILogger<ContributionEndpoints>? logger,
        int discId,
        CancellationToken cancellationToken)
    {
        if (comparisonService is null)
        {
            return;
        }

        try
        {
            await comparisonService.RecompareDiscAsync(discId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Unable to compare MakeMKV log and ODM manifest for contribution disc {DiscId}", discId);
        }
    }

    public async Task<IResult> ClearDiscLogError(IDbContextFactory<SqlServerDataContext> dbContextFactory, IdEncoder idEncoder, string contributionId, string discId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        int realDiscId = idEncoder.Decode(discId);
        var disc = await dbContext.UserContributionDiscs
            .FirstOrDefaultAsync(d => d.Id == realDiscId, cancellationToken);

        if (disc == null)
        {
            return TypedResults.NotFound();
        }

        disc.LogUploadError = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok();
    }

    #region Image Upload (temp storage during creation)

    public async Task<IResult> RemoveFrontImage(Guid id, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore service, CancellationToken cancellationToken)
        => await RemoveImage(id, "front", service, cancellationToken);

    public async Task<IResult> UploadFrontImage(IFormFileCollection files, Guid id, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore service, CancellationToken cancellationToken)
        => await UploadImage(files, id, "front", service, cancellationToken);

    public async Task<IResult> RemoveBackImage(Guid id, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore service, CancellationToken cancellationToken)
        => await RemoveImage(id, "back", service, cancellationToken);

    public async Task<IResult> UploadBackImage(IFormFileCollection files, Guid id, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore service, CancellationToken cancellationToken)
        => await UploadImage(files, id, "back", service, cancellationToken);

    public async Task<IResult> RemoveImage(Guid id, string name, IStaticAssetStore service, CancellationToken cancellationToken)
    {
        var path = GetReleaseImagePath(id, name);
        await service.Delete(path, cancellationToken);
        return TypedResults.Ok();
    }

    private async Task<IResult> UploadImage(IFormFileCollection files, Guid id, string name, IStaticAssetStore service, CancellationToken cancellationToken)
    {
        var file = files.FirstOrDefault();

        if (file == null || file.Length == 0)
        {
            return Results.BadRequest("No file uploaded.");
        }

        var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        memoryStream.Position = 0;
        string path = GetReleaseImagePath(id, name);
        var result = await service.Save(memoryStream, path, file.ContentType, cancellationToken);

        return TypedResults.Ok();
    }

    private static string GetReleaseImagePath(Guid id, string name) => $"Contributions/releaseImages/{id}/{name}.jpg";

    #endregion

    #region Image Upload (direct replacement for existing contributions)

    private static readonly UserContributionStatus[] EditableStatuses =
    [
        UserContributionStatus.Pending,
        UserContributionStatus.ChangesRequested,
        UserContributionStatus.Rejected
    ];

    public async Task<IResult> UploadContributionFrontImage(
        IFormFileCollection files,
        string contributionId,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => await UploadContributionImage(files, contributionId, "front", dbFactory, idEncoder, userManager, imageStore, assetStore, httpContext, cancellationToken);

    public async Task<IResult> UploadContributionBackImage(
        IFormFileCollection files,
        string contributionId,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => await UploadContributionImage(files, contributionId, "back", dbFactory, idEncoder, userManager, imageStore, assetStore, httpContext, cancellationToken);

    public async Task<IResult> DeleteContributionBackImage(
        string contributionId,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var (contribution, error) = await VerifyContributionOwnership(contributionId, db, idEncoder, userManager, httpContext, cancellationToken);
        if (error != null) return error;

        string encodedId = idEncoder.Encode(contribution!.Id);
        await imageStore.Delete($"Contributions/{encodedId}/back.jpg", cancellationToken);
        await assetStore.Delete($"{encodedId}/back.jpg", cancellationToken);

        contribution.BackImageUrl = null;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }

    private async Task<IResult> UploadContributionImage(
        IFormFileCollection files,
        string contributionId,
        string name,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var file = files.FirstOrDefault();
        if (file == null || file.Length == 0)
            return Results.BadRequest("No file uploaded.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var (contribution, error) = await VerifyContributionOwnership(contributionId, db, idEncoder, userManager, httpContext, cancellationToken);
        if (error != null) return error;

        string encodedId = idEncoder.Encode(contribution!.Id);

        // Delete existing blobs first — Save() skips upload if blob already exists
        string imageStorePath = $"Contributions/{encodedId}/{name}.jpg";
        string assetStorePath = $"{encodedId}/{name}.jpg";
        await imageStore.Delete(imageStorePath, cancellationToken);
        await assetStore.Delete(assetStorePath, cancellationToken);

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        memoryStream.Position = 0;
        await imageStore.Save(memoryStream, imageStorePath, ContentTypes.ImageContentType, cancellationToken);

        memoryStream.Position = 0;
        await assetStore.Save(memoryStream, assetStorePath, ContentTypes.ImageContentType, cancellationToken);

        string imageUrl = $"/images/Contributions/{encodedId}/{name}.jpg";
        if (name == "front")
            contribution.FrontImageUrl = imageUrl;
        else
            contribution.BackImageUrl = imageUrl;

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new { imageUrl });
    }

    private static async Task<(UserContribution? contribution, IResult? error)> VerifyContributionOwnership(
        string contributionId,
        SqlServerDataContext db,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        int decodedId = idEncoder.Decode(contributionId);
        if (decodedId == 0)
            return (null, TypedResults.BadRequest("Invalid contribution ID."));

        var contribution = await db.UserContributions
            .FirstOrDefaultAsync(c => c.Id == decodedId, cancellationToken);

        if (contribution == null)
            return (null, TypedResults.NotFound("Contribution not found."));

        var userId = userManager.GetUserId(httpContext.User);
        if (string.IsNullOrEmpty(userId) || contribution.UserId != userId)
            return (null, TypedResults.Forbid());

        if (!EditableStatuses.Contains(contribution.Status))
            return (null, TypedResults.BadRequest($"Cannot edit images for a contribution with status '{contribution.Status}'."));

        return (contribution, null);
    }

    #endregion

    #region Boxset Image Upload

    public async Task<IResult> UploadBoxsetFrontImage(
        IFormFileCollection files,
        string boxsetId,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => await UploadBoxsetImage(files, boxsetId, "front", dbFactory, idEncoder, userManager, imageStore, assetStore, httpContext, cancellationToken);

    public async Task<IResult> UploadBoxsetBackImage(
        IFormFileCollection files,
        string boxsetId,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => await UploadBoxsetImage(files, boxsetId, "back", dbFactory, idEncoder, userManager, imageStore, assetStore, httpContext, cancellationToken);

    public async Task<IResult> DeleteBoxsetFrontImage(
        string boxsetId,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => await DeleteBoxsetImage(boxsetId, "front", dbFactory, idEncoder, userManager, imageStore, assetStore, httpContext, cancellationToken);

    public async Task<IResult> DeleteBoxsetBackImage(
        string boxsetId,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => await DeleteBoxsetImage(boxsetId, "back", dbFactory, idEncoder, userManager, imageStore, assetStore, httpContext, cancellationToken);

    private async Task<IResult> UploadBoxsetImage(
        IFormFileCollection files,
        string boxsetId,
        string name,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var file = files.FirstOrDefault();
        if (file == null || file.Length == 0)
            return Results.BadRequest("No file uploaded.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var (boxset, error) = await VerifyBoxsetOwnership(boxsetId, db, idEncoder, userManager, httpContext, cancellationToken);
        if (error != null) return error;

        string encodedId = idEncoder.Encode(boxset!.Id);

        string imageStorePath = $"Contributions/Boxsets/{encodedId}/{name}.jpg";
        string assetStorePath = $"Boxsets/{encodedId}/{name}.jpg";
        await imageStore.Delete(imageStorePath, cancellationToken);
        await assetStore.Delete(assetStorePath, cancellationToken);

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        memoryStream.Position = 0;
        await imageStore.Save(memoryStream, imageStorePath, ContentTypes.ImageContentType, cancellationToken);

        memoryStream.Position = 0;
        await assetStore.Save(memoryStream, assetStorePath, ContentTypes.ImageContentType, cancellationToken);

        string imageUrl = $"/api/contribute/images/Contributions/Boxsets/{encodedId}/{name}.jpg";
        if (name == "front")
            boxset.FrontImageUrl = imageUrl;
        else
            boxset.BackImageUrl = imageUrl;

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new { imageUrl });
    }

    private async Task<IResult> DeleteBoxsetImage(
        string boxsetId,
        string name,
        IDbContextFactory<SqlServerDataContext> dbFactory,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        IStaticAssetStore imageStore,
        IStaticAssetStore assetStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var (boxset, error) = await VerifyBoxsetOwnership(boxsetId, db, idEncoder, userManager, httpContext, cancellationToken);
        if (error != null) return error;

        string encodedId = idEncoder.Encode(boxset!.Id);
        await imageStore.Delete($"Contributions/Boxsets/{encodedId}/{name}.jpg", cancellationToken);
        await assetStore.Delete($"Boxsets/{encodedId}/{name}.jpg", cancellationToken);

        if (name == "front")
            boxset.FrontImageUrl = null;
        else
            boxset.BackImageUrl = null;

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }

    private static async Task<(UserContributionBoxset? boxset, IResult? error)> VerifyBoxsetOwnership(
        string boxsetId,
        SqlServerDataContext db,
        IdEncoder idEncoder,
        UserManager<TheDiscDbUser> userManager,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        int decodedId = idEncoder.Decode(boxsetId);
        if (decodedId == 0)
            return (null, TypedResults.BadRequest("Invalid boxset ID."));

        var boxset = await db.UserContributionBoxsets
            .FirstOrDefaultAsync(b => b.Id == decodedId, cancellationToken);

        if (boxset == null)
            return (null, TypedResults.NotFound("Boxset not found."));

        var userId = userManager.GetUserId(httpContext.User);
        if (string.IsNullOrEmpty(userId) || boxset.UserId != userId)
            return (null, TypedResults.Forbid());

        if (!EditableStatuses.Contains(boxset.Status))
            return (null, TypedResults.BadRequest($"Cannot edit images for a boxset with status '{boxset.Status}'."));

        return (boxset, null);
    }

    #endregion

    #region Direct image serving (bypasses ImageSharp caching)

    public async Task<IResult> ServeContributionImage(
        string path,
        [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore imageStore,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            return TypedResults.BadRequest("Invalid image path.");

        try
        {
            var data = await imageStore.Download(path, cancellationToken);
            httpContext.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(data.ToArray(), "image/jpeg");
        }
        catch
        {
            return TypedResults.NotFound();
        }
    }

    #endregion
}
