using System.Text;
using FluentResults;
using MakeMkv;
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
                        _ = LogParser.Parse(allLines);

                    }
                    catch (Exception)
                    {
                        return Result.Fail($"Could not parse log file");
                    }
                }

                // TODO: if the logs have changed, rewrite the memorystream

                //Save the logs in blob storage
                memoryStream.Position = 0;
                await assetStore.Save(memoryStream, $"{contributionId}/{idEncoder.Encode(disc.Id)}-logs.txt", ContentTypes.TextContentType, cancellationToken);
            }

            disc.LogsUploaded = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();

        //TODO: Notify the client a disc has been added? (to prevent the client having to poll)
    }

    #region Image Upload

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
}
