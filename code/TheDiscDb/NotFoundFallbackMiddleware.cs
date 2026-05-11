using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace TheDiscDb;

/// <summary>
/// Writes a static HTML body for 404 responses that the framework would otherwise
/// serve with an empty body (unmatched routes, Blazor SSR pages that set
/// <c>StatusCode = 404</c> in <c>OnInitializedAsync</c>). Bodyless 404s are flagged
/// by Google as "soft 404", which hurts SEO.
///
/// Scoped to browser document requests only — API/asset paths and non-text/html
/// Accept headers keep their original 404 shape so API contracts aren't disturbed.
///
/// The HTML body lives in <c>wwwroot/404.html</c> so it is editable as a real file
/// (and is also served directly by the static file middleware at <c>/404.html</c>).
/// </summary>
public sealed class NotFoundFallbackMiddleware
{
    // Paths whose 404 responses must remain machine-readable (empty / JSON / etc.)
    // because they are consumed by APIs, not browsers.
    private static readonly string[] SkipPathPrefixes =
    [
        "/graphql",
        "/api",
        "/images",
        "/_framework",
        "/_content",
        "/_blazor",
    ];

    private readonly RequestDelegate next;
    private readonly byte[] bodyBytes;
    private readonly ILogger<NotFoundFallbackMiddleware> logger;

    public NotFoundFallbackMiddleware(
        RequestDelegate next,
        IWebHostEnvironment env,
        ILogger<NotFoundFallbackMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(env);

        this.next = next;
        this.logger = logger;
        this.bodyBytes = LoadBody(env, logger);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await this.next(context);

        if (!ShouldHandle(context))
        {
            return;
        }

        context.Response.ContentLength = null;
        context.Response.ContentType = "text/html; charset=utf-8";

        // Per RFC 7231 §4.3.2, HEAD responses must not include a body. Emit only
        // the headers so crawlers still learn we have a meaningful page here.
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.Body.WriteAsync(this.bodyBytes, context.RequestAborted);
    }

    private static bool ShouldHandle(HttpContext context)
    {
        if (context.Response.StatusCode != StatusCodes.Status404NotFound
            || context.Response.HasStarted
            || (context.Response.ContentLength != null && context.Response.ContentLength != 0)
            || !string.IsNullOrEmpty(context.Response.ContentType))
        {
            return false;
        }

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value;
        if (path is not null)
        {
            foreach (var prefix in SkipPathPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // Browsers and crawlers send Accept containing text/html (or */*).
        // Raw API clients usually send application/json — skip those.
        var accept = context.Request.Headers.Accept.ToString();
        if (!string.IsNullOrEmpty(accept)
            && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            && !accept.Contains("*/*", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static byte[] LoadBody(IWebHostEnvironment env, ILogger logger)
    {
        const string FileName = "404.html";
        var fileInfo = env.WebRootFileProvider.GetFileInfo(FileName);
        if (!fileInfo.Exists)
        {
            logger.LogWarning(
                "404 fallback file 'wwwroot/{FileName}' not found; falling back to a minimal inline body.",
                FileName);
            return Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Page Not Found</title></head><body><h1>Page not found</h1><p><a href=\"/\">Return home</a></p></body></html>");
        }

        using var stream = fileInfo.CreateReadStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
