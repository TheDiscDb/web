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
        "/images"
    ];

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value;

        // Redirect GET requests with uppercase characters in the path to their lowercase equivalent,
        // but skip paths that contain case-sensitive encoded IDs (e.g. Sqids)
        if (request.Method == HttpMethods.Get
            && !string.IsNullOrEmpty(path)
            && path.Any(char.IsUpper)
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
