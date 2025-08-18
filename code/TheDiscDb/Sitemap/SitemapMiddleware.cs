namespace TheDiscDb.Web.Sitemap;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseSitemap(this IApplicationBuilder builder) => UseMiddlewareExtensions.UseMiddleware<SitemapMiddleware>(builder, Array.Empty<object>());
}

public class SitemapMiddleware
{
    private readonly RequestDelegate next;
    private readonly SitemapGenerator generator;
    private readonly IMemoryCache cache;

    public SitemapMiddleware(RequestDelegate next, SitemapGenerator generator, IMemoryCache cache)
    {
        this.next = next ?? throw new ArgumentNullException(nameof(next));
        this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsSiteMapRequested(context))
        {
            await this.WriteSitemapAsync(context);
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

    private async Task WriteSitemapAsync(HttpContext context)
    {
        string? xml = await this.cache.GetOrCreateAsync("sitemap", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            IEnumerable<SitemapNode> validUrls = await this.generator.Build(this.GetSiteBaseUrl(context.Request));
            return BuildSitemapXml(validUrls);
        });

        if (xml != null)
        {
            await WriteStringContentAsync(context, xml, "application/xml");
        }
    }

    private async Task WriteGroupsSitemapAsync(HttpContext context)
    {
        string? xml = await this.cache.GetOrCreateAsync("groups-sitemap", async entry =>
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
            stringBuilder.AppendFormat("<loc>{0}</loc>\r\n", node.Url);

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

    private Task WriteRobotsAsync(HttpContext context)
    {
        string content = $"User-agent: *\r\nAllow: /\r\nSitemap: {this.GetSitemapUrl(context.Request)}\r\nSitemap: {this.GetGroupsSitemapUrl(context.Request)}";
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
