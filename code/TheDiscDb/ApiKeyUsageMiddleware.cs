using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Authentication;
using TheDiscDb.Web.Data;

namespace TheDiscDb;

public class ApiKeyUsageMiddleware(
    RequestDelegate next,
    ApiKeyManager apiKeyManager,
    IDbContextFactory<SqlServerDataContext> dbContextFactory,
    ILogger<ApiKeyUsageMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        var startTime = Stopwatch.GetTimestamp();

        await next(context);

        var apiKeyIdClaim = context.User?.FindFirst("ApiKeyId")?.Value;
        if (string.IsNullOrEmpty(apiKeyIdClaim) || !int.TryParse(apiKeyIdClaim, out var apiKeyId))
        {
            return;
        }

        var apiKey = await apiKeyManager.TryGetByIdAsync(apiKeyId);
        if (apiKey is not { LogUsage: true })
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(startTime);
        var durationMs = (int)elapsed.TotalMilliseconds;

        var operationName = await ExtractOperationNameAsync(context.Request);

        // Cost fields default to 0 until HotChocolate v16+ exposes OperationCost diagnostic events
        _ = LogUsageAsync(apiKeyId, operationName, durationMs)
            .ContinueWith(task =>
            {
                logger.LogWarning(task.Exception, "Failed to log API key usage for key {KeyId}", apiKeyId);
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static async Task<string?> ExtractOperationNameAsync(HttpRequest request)
    {
        if (request.Query.TryGetValue("operationName", out var opName))
        {
            return opName.ToString();
        }

        if (request.Method == HttpMethods.Post
            && request.Body.CanSeek
            && request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            request.Body.Position = 0;
            try
            {
                using var doc = await JsonDocument.ParseAsync(request.Body);
                if (doc.RootElement.TryGetProperty("operationName", out var op) && op.ValueKind == JsonValueKind.String)
                {
                    return op.GetString();
                }
            }
            catch
            {
                // Body may not be valid JSON; ignore
            }
        }

        return null;
    }

    private async Task LogUsageAsync(int apiKeyId, string? operationName, int durationMs)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.ApiKeyUsageLogs.Add(new ApiKeyUsageLog
        {
            ApiKeyId = apiKeyId,
            Timestamp = DateTimeOffset.UtcNow,
            OperationName = operationName,
            FieldCost = 0,
            TypeCost = 0,
            DurationMs = durationMs
        });
        await db.SaveChangesAsync();
    }
}
