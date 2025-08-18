namespace TheDiscDb.Web;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public class RssFeedMidleware
{
    private readonly RequestDelegate next;
    private readonly IDbContextFactory<SqlServerDataContext> dbFactory;
    private readonly IMemoryCache cache;

    public RssFeedMidleware(RequestDelegate next, IDbContextFactory<SqlServerDataContext> dbFactory, IMemoryCache cache)
    {
        this.next = next ?? throw new ArgumentNullException(nameof(next));
        this.dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request?.Path.Value != null && context.Request.Path.Value.StartsWith("/rss", StringComparison.OrdinalIgnoreCase))
        {
            var stringWriter = new StringWriterWithEncoding(Encoding.UTF8);
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                NewLineHandling = NewLineHandling.Entitize,
                NewLineOnAttributes = true,
                Indent = true
            };

            using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, settings))
            {
                var mediaItems = await this.GetReleaseItems();

                var syndicationItems = new List<SyndicationItem>();

                foreach (var e in mediaItems)
                {
                    if (e.MediaItem?.Type == null)
                    {
                        continue;
                    }

                    string title = $"{e.MediaItem.FullTitle} - {e.Title}";
                    string url = $"https://thediscdb.com/{e.MediaItem.Type.ToLower()}/{e.MediaItem.Slug}/releases/{e.Slug}?utm_source=rss&utm_content=latestReleases&utm_medium=RSS2";
                    string contentHtml = this.CreateHtmlContent(e);
                    string slug = $"{e.MediaItem.Type}-{e.MediaItem.Slug}-{e.Slug}";

                    var item = new SyndicationItem(title, new TextSyndicationContent(contentHtml, TextSyndicationContentKind.Html), new Uri(url), slug, e.DateAdded);
                    syndicationItems.Add(item);
                }

                DateTimeOffset dateAdded = mediaItems.First().DateAdded;
                var feed = new SyndicationFeed("TheDiscDB Latest Releases", "The latest additions to TheDiscDb", new Uri("https://thediscdb.com"), "https://thediscdb.com/rss", dateAdded, syndicationItems);

                var rssFormatter = new Rss20FeedFormatter(feed, false);
                rssFormatter.WriteTo(xmlWriter);
                xmlWriter.Flush();
            }

            context.Response.ContentType = "application/rss+xml; charset=utf-8";
            await context.Response.WriteAsync(stringWriter.ToString());
        }

        await next(context);
    }

    private async Task<IEnumerable<Release>> GetReleaseItems()
    {
        var items = await this.cache.GetOrCreateAsync("rssFeed", async cacheItem =>
        {
            using (var db = this.dbFactory.CreateDbContext())
            {
                var mediaItems = db.Releases
                    .Include(r => r.MediaItem)
                    .Include(r => r.Discs)
                    .ThenInclude(d => d.Titles)
                    .ThenInclude(t => t.Item)
                    .OrderByDescending(r => r.DateAdded)
                    .Take(50);

                cacheItem.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                return await mediaItems.ToListAsync();
            }
        });

        if (items == null)
        {
            return Array.Empty<Release>();
        }

        return items;
    }

    private string CreateHtmlContent(Release release)
    {
        string title = $"{release.MediaItem!.FullTitle} - {release.Title}";
        const int width = 41;
        const int height = 525;
        string imageUrl = $"https://thediscdb.com/images/{release.ImageUrl}?width={width}3&height={height}";

        var s = new StringBuilder();
        s.Append($"<div><img src=\"${imageUrl}\" alt=\"${title}\" width=\"{width}\" height=\"{height}\" /></div><br />");
        s.Append($"<div>{title} was released on {release.ReleaseDate:d} was added on {release.DateAdded:d}</div><div>");

        foreach (var disc in release.Discs)
        {
            s.Append($"<div>Disc {disc.Index} ({disc.Format})");
            if (!string.IsNullOrEmpty(disc.Name))
            {
                s.Append($" - {disc.Name}");
                s.Append("<ul>");
                var features = this.GetDiscFeatures(disc);
                foreach (var feature in features)
                {
                    s.Append($"<li>{feature.GetDescription()}</li>");
                }
                s.Append("</ul>");
            }
            s.Append("</div>");
        }

        s.Append("</div>");

        return s.ToString();
    }

    private List<Feature> GetDiscFeatures(Disc disc)
    {
        var features = new List<Feature>();

        foreach (var title in disc.Titles.Where(t => t.Item != null))
        {
            if (title.Item?.Type == null)
            {
                continue;
            }

            var feature = features.FirstOrDefault(f => f.Type == title.Item.Type);

            if (feature == null)
            {
                feature = new Feature(title.Item.Type)
                {
                    HasChapters = title.Item.Chapters.Any(),
                    Count = 1
                };

                features.Add(feature);
            }
            else
            {
                feature.Count++;
            }
        }

        return features;
    }
}

class Feature
{
    public string Type { get; set; }
    public int Count { get; set; }
    public bool HasChapters { get; set; }

    public Feature(string type)
    {
        this.Type = type;
    }

    public string GetDescription() => this switch
    {
        { Type: "DeletedScene", Count: var c } when c == 1 => "1 deleted scene",
        { Type: "DeletedScene", Count: var c } => $"{c} deleted scenes",
        { Type: var t, Count: var c } when c == 1 => $"{c} {t.ToLower()}",
        { Type: var t, Count: var c } => $"{c} {t.ToLower()}s"
    };
}

class StringWriterWithEncoding : StringWriter
{
    private readonly Encoding encoding;

    public StringWriterWithEncoding(Encoding encoding)
    {
        this.encoding = encoding;
    }

    public override Encoding Encoding
    {
        get { return encoding; }
    }
}
