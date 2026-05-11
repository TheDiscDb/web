using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace TheDiscDb;

/// <summary>
/// Detects search-engine crawlers and other bots by User-Agent so SSR pages can
/// emit static, indexable HTML for them while serving the richer interactive
/// (WASM) experience to real users.
/// </summary>
public static partial class CrawlerDetector
{
    // Single regex covering the major crawlers we care about for SEO + link
    // unfurling. Match is case-insensitive and substring-based.
    [GeneratedRegex(
        @"googlebot|google-inspectiontool|bingbot|slurp|duckduckbot|baiduspider|yandex(bot|images)|sogou|exabot|facebookexternalhit|facebot|twitterbot|linkedinbot|embedly|quora link preview|showyoubot|outbrain|pinterest(bot|/|\.)|slackbot|vkshare|w3c_validator|applebot|telegrambot|discordbot|petalbot|semrushbot|ahrefsbot|mj12bot|dotbot|seznambot|chrome-lighthouse|google page speed|gptbot|oai-searchbot|chatgpt-user|perplexitybot|claudebot|anthropic|ccbot|amazonbot|bytespider",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex CrawlerRegex();

    public static bool IsCrawler(HttpContext? context)
    {
        if (context == null)
        {
            // No HttpContext means we're past the prerender phase (interactive
            // WASM/server). Real bots only ever see the prerender response, so
            // treat "no context" as "human".
            return false;
        }

        var ua = context.Request.Headers.UserAgent.ToString();
        return IsCrawler(ua);
    }

    public static bool IsCrawler(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return false;
        }

        try
        {
            return CrawlerRegex().IsMatch(userAgent);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
