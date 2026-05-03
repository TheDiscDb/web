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
}
