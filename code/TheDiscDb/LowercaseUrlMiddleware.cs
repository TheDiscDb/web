namespace TheDiscDb;

public class LowercaseUrlMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value;

        // Redirect GET requests with uppercase characters in the path to their lowercase equivalent
        if (request.Method == HttpMethods.Get
            && !string.IsNullOrEmpty(path)
            && path.Any(char.IsUpper))
        {
            var lowercasePath = path.ToLowerInvariant();
            var newUrl = $"{request.Scheme}://{request.Host}{lowercasePath}{request.QueryString}";

            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = newUrl;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
