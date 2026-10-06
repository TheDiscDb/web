namespace TheDiscDb.Web.Sitemap;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseSitemap(this IApplicationBuilder builder) => UseMiddlewareExtensions.UseMiddleware<SitemapMiddleware>(builder, Array.Empty<object>());
}

public class SitemapMiddleware
{
    // Matches /sitemap-{stem}.xml (e.g. /sitemap-movies-discs.xml, /sitemap-movies-discs-2.xml),
    // but not the bare "/sitemap.xml" index itself (no leading hyphen there).
    private static readonly Regex CategorySitemapPattern = new(@"^/sitemap-(?<stem>[a-z0-9-]+)\.xml$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly RequestDelegate next;
    private readonly SitemapGenerator generator;
    private readonly IMemoryCache cache;

    public SitemapMiddleware(RequestDelegate next, SitemapGenerator generator, IMemoryCache cache)
    {
        this.next = next ?? throw new ArgumentNullException(nameof(next));
        this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    // Holds the paginated, per-category sitemap nodes for a single host, cached for 15 minutes.
    // "PagesByStem" is keyed by the file stem used in /sitemap-{stem}.xml (e.g. "movies-discs",
    // "movies-discs-2"); "OrderedStems" preserves category/page order for the sitemap index.
    private sealed record SitemapCacheEntry(IReadOnlyDictionary<string, IReadOnlyList<SitemapNode>> PagesByStem, IReadOnlyList<string> OrderedStems);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsSiteMapRequested(context))
        {
            await this.WriteSitemapIndexAsync(context);
        }
        else if (TryGetCategorySitemapStem(context, out string stem))
        {
            await this.WriteCategorySitemapAsync(context, stem);
        }
        else if (IsGroupsSiteMapRequested(context))
        {
            await this.WriteGroupsSitemapAsync(context);
        }
        else if (IsRobotsRequested(context))
        {
            await this.WriteRobotsAsync(context);
        }
        else
        {
            await this.next.Invoke(context);
        }
    }

    private async Task<SitemapCacheEntry> GetOrBuildSitemapCacheAsync(HttpContext context)
    {
        string cacheKey = $"sitemap-pages-{context.Request.Host}";
        SitemapCacheEntry? cacheEntry = await this.cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);

            var categories = await this.generator.BuildByCategory(this.GetSiteBaseUrl(context.Request));

            var pagesByStem = new Dictionary<string, IReadOnlyList<SitemapNode>>(StringComparer.OrdinalIgnoreCase);
            var orderedStems = new List<string>();

            foreach (string category in SitemapCategories.All)
            {
                IReadOnlyList<SitemapNode> nodes = categories.TryGetValue(category, out var categoryNodes)
                    ? categoryNodes
                    : Array.Empty<SitemapNode>();

                foreach (SitemapPage page in SitemapPaginator.Paginate(category, nodes))
                {
                    pagesByStem[page.Stem] = page.Nodes;
                    orderedStems.Add(page.Stem);
                }
            }

            return new SitemapCacheEntry(pagesByStem, orderedStems);
        });

        return cacheEntry ?? new SitemapCacheEntry(new Dictionary<string, IReadOnlyList<SitemapNode>>(), Array.Empty<string>());
    }

    private async Task WriteSitemapIndexAsync(HttpContext context)
    {
        SitemapCacheEntry cacheEntry = await this.GetOrBuildSitemapCacheAsync(context);
        string xml = BuildSitemapIndexXml(this.GetSiteBaseUrl(context.Request), cacheEntry.OrderedStems);
        await WriteStringContentAsync(context, xml, "application/xml");
    }

    private async Task WriteCategorySitemapAsync(HttpContext context, string stem)
    {
        SitemapCacheEntry cacheEntry = await this.GetOrBuildSitemapCacheAsync(context);
        if (!cacheEntry.PagesByStem.TryGetValue(stem, out IReadOnlyList<SitemapNode>? nodes))
        {
            // Unknown stem (e.g. stale/guessed URL) — fall through to normal 404 handling.
            await this.next.Invoke(context);
            return;
        }

        string xml = BuildSitemapXml(nodes);
        await WriteStringContentAsync(context, xml, "application/xml");
    }

    private async Task WriteGroupsSitemapAsync(HttpContext context)
    {
        string cacheKey = $"groups-sitemap-{context.Request.Host}";
        string? xml = await this.cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            IEnumerable<SitemapNode> validUrls = await this.generator.BuildGroupsMap(this.GetSiteBaseUrl(context.Request));
            return BuildSitemapXml(validUrls);
        });

        if (xml != null)
        {
            await WriteStringContentAsync(context, xml, "application/xml");
        }
    }

    private static string BuildSitemapXml(IEnumerable<SitemapNode> nodes)
    {
        StringBuilder stringBuilder = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\r\n");

        foreach (SitemapNode node in nodes)
        {
            stringBuilder.AppendLine("<url>");
            stringBuilder.AppendFormat("<loc>{0}</loc>\r\n", SecurityElement.Escape(node.Url));

            if (node.Frequency.HasValue)
            {
                stringBuilder.AppendFormat("<changefreq>{0}</changefreq>\r\n", node.Frequency.Value.ToString().ToLower());
            }

            if (node.LastModified.HasValue)
            {
                stringBuilder.AppendFormat("<lastmod>{0}</lastmod>", node.LastModified.Value.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz"));
            }

            if (node.Priority.HasValue)
            {
                stringBuilder.AppendFormat("<priority>{0}</priority>\r\n", node.Priority);
            }

            stringBuilder.AppendLine("</url>");
        }

        stringBuilder.Append("</urlset>");

        return stringBuilder.ToString();
    }

    private static string BuildSitemapIndexXml(string siteBase, IEnumerable<string> stems)
    {
        StringBuilder stringBuilder = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\r\n");

        foreach (string stem in stems)
        {
            stringBuilder.AppendLine("<sitemap>");
            stringBuilder.AppendFormat("<loc>{0}</loc>\r\n", SecurityElement.Escape($"{siteBase}/sitemap-{stem}.xml"));
            stringBuilder.AppendLine("</sitemap>");
        }

        stringBuilder.Append("</sitemapindex>");

        return stringBuilder.ToString();
    }

    private Task WriteRobotsAsync(HttpContext context)
    {
        // These paths all require an authenticated user ([Authorize]) and, when crawled while
        // signed out, return a 302 to the login page — which Search Console flags as
        // "Page with redirect". Keep crawlers off them entirely rather than relying on noindex,
        // since the redirect happens before any page content (and any noindex tag) is served.
        string content = $"User-agent: *\r\nAllow: /\r\nDisallow: /*/edit\r\nDisallow: /admin/\r\nDisallow: /admin\r\nDisallow: /contribute/\r\nDisallow: /contribution/\r\nDisallow: /changes/my\r\nSitemap: {this.GetSitemapUrl(context.Request)}\r\nSitemap: {this.GetGroupsSitemapUrl(context.Request)}";
        return WriteStringContentAsync(context, content, "text/plain");
    }

    private string GetSitemapUrl(HttpRequest contextRequest) => this.GetSiteBaseUrl(contextRequest) + "/sitemap.xml";
    private string GetGroupsSitemapUrl(HttpRequest contextRequest) => this.GetSiteBaseUrl(contextRequest) + "/groups.xml";

    private static bool IsSiteMapRequested(HttpContext context)
    {
        if (!context.Request.Path.HasValue)
        {
            return false;
        }

        return context.Request.Path.Value.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetCategorySitemapStem(HttpContext context, out string stem)
    {
        stem = string.Empty;
        if (!context.Request.Path.HasValue)
        {
            return false;
        }

        Match match = CategorySitemapPattern.Match(context.Request.Path.Value);
        if (!match.Success)
        {
            return false;
        }

        stem = match.Groups["stem"].Value;
        return true;
    }

    private static bool IsGroupsSiteMapRequested(HttpContext context)
    {
        if (!context.Request.Path.HasValue)
        {
            return false;
        }

        return context.Request.Path.Value.Equals("/groups.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRobotsRequested(HttpContext context)
    {
        if (!context.Request.Path.HasValue)
        {
            return false;
        }

        return context.Request.Path.Value.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteStringContentAsync(HttpContext context, string content, string contentType)
    {
        Stream body = context.Response.Body;
        context.Response.StatusCode = 200;
        context.Response.ContentType = contentType;
        using (MemoryStream memoryStream = new MemoryStream())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            memoryStream.Write(bytes, 0, bytes.Length);
            memoryStream.Seek(0L, 0);
            await memoryStream.CopyToAsync(body, bytes.Length);
        }
    }

    private string GetSiteBaseUrl(HttpRequest request)
    {
        return string.Format("{0}://{1}{2}", request.Scheme, request.Host, request.PathBase);
    }
}
