namespace TheDiscDb;

public class LowercaseUrlMiddleware(RequestDelegate next)
{
    // Paths that contain case-sensitive encoded IDs and must not be lowercased
    private static readonly string[] CaseSensitivePrefixes =
    [
        "/contribution",
        "/admin",
        "/api",
        "/graphql",
        "/images",
        "/_framework",
        "/_content"
    ];

    // Static asset path segments that may appear embedded in page URLs
    // due to cached pages with relative (non-root) asset references
    private static readonly string[] StaticAssetSegments =
    [
        "/_content/",
        "/_framework/"
    ];

    // Static file extensions that should never reach Blazor routing.
    // Browsers may request these relative to deep page URLs when serving
    // cached pages that still have relative asset references.
    private static readonly string[] StaticFileExtensions =
    [
        ".css", ".js", ".map", ".mjs",
        ".woff", ".woff2", ".ttf", ".eot",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico",
        ".json", ".webmanifest", ".wasm", ".dll"
    ];

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value;

        if (string.IsNullOrEmpty(path) || request.Method != HttpMethods.Get)
        {
            return next(context);
        }

        // Redirect requests with static asset segments embedded at a non-root position.
        // This happens when cached pages have relative asset references that the browser
        // resolves against the page URL (e.g. /movie/.../discs/_content/Foo/bar.js).
        var correctedPath = TryExtractEmbeddedAssetPath(path);
        if (correctedPath is not null)
        {
            var newUrl = $"{request.Scheme}://{request.Host}{correctedPath}{request.QueryString}";
            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = newUrl;
            return Task.CompletedTask;
        }

        // Short-circuit requests for static file extensions embedded in page URLs.
        // When a cached page has a relative asset href (e.g. "thediscdb.xyz.styles.css"),
        // the browser resolves it against the page path, producing URLs like
        // /series/.../discs/00000/thediscdb.xyz.styles.css — which would otherwise
        // match Blazor catch-all routes and return misleading 404 pages.
        if (IsStaticFileRequest(path) && !ContainsCaseSensitiveSegments(path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        // Redirect requests with uppercase characters in the path to their lowercase equivalent,
        // but skip paths that contain case-sensitive encoded IDs (e.g. Sqids)
        if (path.Any(char.IsUpper)
            && !ContainsCaseSensitiveSegments(path))
        {
            var lowercasePath = path.ToLowerInvariant();
            var newUrl = $"{request.Scheme}://{request.Host}{lowercasePath}{request.QueryString}";

            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = newUrl;
            return Task.CompletedTask;
        }

        return next(context);
    }

    private static string? TryExtractEmbeddedAssetPath(string path)
    {
        foreach (var segment in StaticAssetSegments)
        {
            var index = path.IndexOf(segment, 1, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return path[index..];
            }
        }

        return null;
    }

    private static bool ContainsCaseSensitiveSegments(string path)
    {
        foreach (var prefix in CaseSensitivePrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStaticFileRequest(string path)
    {
        // Only treat paths with multiple segments as misrouted static files.
        // Root-level paths like /favicon.ico are legitimate and should pass through.
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            return false;
        }

        foreach (var ext in StaticFileExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
