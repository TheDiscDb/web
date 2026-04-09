using System.Text;
using FluentResults;
using MakeMkv;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web;

public class ContributionEndpoints
{
    public void MapEndpoints(WebApplication app)
    {
        var contribute = app.MapGroup("/api/contribute").RequireAuthorization();

        contribute.MapPost("{contributionId}/discs/{discId}/logs", SaveDiscLogs)
            .AllowAnonymous()
            .Accepts<string>("text/plain");
        contribute.MapDelete("{contributionId}/discs/{discId}/logs/error", ClearDiscLogError)
            .AllowAnonymous();
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
    }

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

    public async Task<IResult> SaveDiscLogs(IDbContextFactory<SqlServerDataContext> dbContextFactory, IdEncoder idEncoder, IStaticAssetStore assetStore, HttpRequest request, string contributionId, string discId, CancellationToken cancellationToken)
    {
        using (StreamReader reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            string logs = await reader.ReadToEndAsync();
            var result = await SaveDiscLogsInternal(dbContextFactory, idEncoder, assetStore, contributionId, discId, logs, cancellationToken);
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

    public async Task<Result> SaveDiscLogsInternal(IDbContextFactory<SqlServerDataContext> dbContextFactory, IdEncoder idEncoder, IStaticAssetStore assetStore, string contributionId, string discId, string logs, CancellationToken cancellationToken)
    {
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
        }

        return Result.Ok();

        //TODO: Notify the client a disc has been added? (to prevent the client having to poll)
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
}
