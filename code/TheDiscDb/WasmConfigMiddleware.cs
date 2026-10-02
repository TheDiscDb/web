using System.Text.Json;
using System.Text.Json.Nodes;
using TheDiscDb.Services.Contributions;

namespace TheDiscDb;

/// <summary>
/// Intercepts the WASM client's appsettings.json request and merges in
/// server-side configuration values that aren't available as static files.
/// </summary>
public class WasmConfigMiddleware
{
    private readonly RequestDelegate next;
    private readonly byte[]? augmentedJson;

    public WasmConfigMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment env)
    {
        this.next = next;

        var publicApiKey = configuration.GetValue<string>("GraphQL:ApiKeyAuthentication:PublicApiKey");
        var apiKeyAuthEnabled = configuration.GetValue<bool>("GraphQL:ApiKeyAuthentication:Enabled");
        var discScanMode = configuration.GetValue<string>($"{DiscScanOptions.SectionName}:DiscScanMode")
            ?? DiscScanMode.Optional.ToString();
        if ((apiKeyAuthEnabled && !string.IsNullOrEmpty(publicApiKey))
            || !string.IsNullOrEmpty(discScanMode))
        {
            var fileInfo = env.WebRootFileProvider.GetFileInfo("appsettings.json");
            JsonObject json;
            if (fileInfo.Exists && !fileInfo.IsDirectory)
            {
                using var stream = fileInfo.CreateReadStream();
                json = JsonNode.Parse(stream) as JsonObject ?? new JsonObject();
            }
            else
            {
                json = new JsonObject();
            }

            if (apiKeyAuthEnabled && !string.IsNullOrEmpty(publicApiKey))
            {
                var graphql = json["GraphQL"] as JsonObject ?? new JsonObject();
                var auth = graphql["ApiKeyAuthentication"] as JsonObject ?? new JsonObject();
                auth["PublicApiKey"] = publicApiKey;
                graphql["ApiKeyAuthentication"] = auth;
                json["GraphQL"] = graphql;
            }

            var contributions = json[DiscScanOptions.SectionName] as JsonObject ?? new JsonObject();
            contributions[nameof(DiscScanOptions.DiscScanMode)] = discScanMode;
            json[DiscScanOptions.SectionName] = contributions;

            augmentedJson = JsonSerializer.SerializeToUtf8Bytes(json, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (augmentedJson != null
            && string.Equals(context.Request.Path.Value, "/appsettings.json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "application/json";
            context.Response.ContentLength = augmentedJson.Length;
            await context.Response.Body.WriteAsync(augmentedJson);
            return;
        }

        await next(context);
    }
}
