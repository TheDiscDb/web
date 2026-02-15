using System.Text.RegularExpressions;

namespace TheDiscDb;

public partial class LowercaseUrlMiddleware(RequestDelegate next)
{
    [GeneratedRegex(
        @"bot|crawl|spider|slurp|bingpreview|mediapartners-google|adsbot|feedfetcher|google-read-aloud|duckduckbot|baiduspider|yandex|sogou|exabot|facebot|facebookexternalhit|ia_archiver|semrushbot|ahrefsbot|dotbot|rogerbot|linkedinbot|embedly|quora link preview|showyoubot|outbrain|pinterest|applebot|twitterbot|bitlybot|skypeuripreview|nuzzel|discordbot|qwantify|pinterestbot|petalbot|mj12bot|bytespider|gptbot|chatgpt|claudebot|anthropic|cohere-ai|perplexity",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CrawlerPattern();

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value;

        // Only redirect GET requests from crawlers with uppercase characters in the path
        if (request.Method == HttpMethods.Get
            && !string.IsNullOrEmpty(path)
            && path.Any(char.IsUpper)
            && IsCrawler(request.Headers.UserAgent.ToString()))
        {
            var lowercasePath = path.ToLowerInvariant();
            var newUrl = $"{request.Scheme}://{request.Host}{lowercasePath}{request.QueryString}";

            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = newUrl;
            return Task.CompletedTask;
        }

        return next(context);
    }

    private static bool IsCrawler(string userAgent)
    {
        return !string.IsNullOrEmpty(userAgent) && CrawlerPattern().IsMatch(userAgent);
    }
}
