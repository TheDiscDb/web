namespace TheDiscDb.Web.Sitemap;

using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public class SitemapGenerator
{
    private readonly IDbContextFactory<SqlServerDataContext> dbFactory;

    public SitemapGenerator(IDbContextFactory<SqlServerDataContext> dbFactory)
    {
        this.dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<IEnumerable<SitemapNode>> Build(string siteBase)
    {
        var nodes = new HashSet<SitemapNode>();

        using (var dbContext = this.dbFactory.CreateDbContext())
        {
            await this.GenerateMediaItemUrls(siteBase, dbContext, nodes);
            await this.GenerateBoxsetUrls(siteBase, dbContext, nodes);
            //await this.GenerateGroupUrls(siteBase, dbContext, nodes);
        }

        return nodes;
    }

    public async Task<IEnumerable<SitemapNode>> BuildGroupsMap(string siteBase)
    {
        var nodes = new HashSet<SitemapNode>();

        using (var dbContext = this.dbFactory.CreateDbContext())
        {
            await this.GenerateGroupUrls(siteBase, dbContext, nodes);
        }

        return nodes;
    }

    private async Task GenerateGroupUrls(string siteBase, SqlServerDataContext dbContext, ICollection<SitemapNode> results)
    {
        var groups = dbContext.Groups.AsAsyncEnumerable();
        await foreach (var group in groups)
        {
            string relativeUrl = $"/g/{group.Slug}";
            results.Add(CreateSitemapNode(siteBase, relativeUrl));
        }
    }

    private async Task GenerateMediaItemUrls(string siteBase, SqlServerDataContext dbContext, ICollection<SitemapNode> results)
    {
        var mediaItems = dbContext.MediaItems
            .Include(p => p.Releases)
            .ThenInclude(r => r.Discs)
            .ThenInclude(d => d.Titles)
            .ThenInclude(t => t.Item)
            .AsAsyncEnumerable();

        await foreach (var item in mediaItems)
        {
            string relativeUrl = $"/{item.Type}/{item.Slug}";
            results.Add(CreateSitemapNode(siteBase, relativeUrl, priority: 1));

            foreach (var release in item.Releases)
            {
                relativeUrl = $"/{item.Type}/{item.Slug}/releases/{release.Slug}";
                results.Add(CreateSitemapNode(siteBase, relativeUrl));

                foreach (var disc in release.Discs)
                {
                    relativeUrl = $"/{item.Type}/{item.Slug}/releases/{release.Slug}/discs/{disc.Index}";
                    results.Add(CreateSitemapNode(siteBase, relativeUrl));

                    foreach (var title in disc.Titles)
                    {
                        if (title.Item != null)
                        {
                            relativeUrl = $"/{item.Type}/{item.Slug}/releases/{release.Slug}/discs/{disc.Index}/{GetTitleUrl(title)}";
                            results.Add(CreateSitemapNode(siteBase, relativeUrl));
                        }
                    }
                }
            }
        }
    }

    private async Task GenerateBoxsetUrls(string siteBase, SqlServerDataContext dbContext, ICollection<SitemapNode> results)
    {
        var boxsets = dbContext.BoxSets
            .Include(p => p.Release)
            .ThenInclude(r => r!.Discs)
            .ThenInclude(d => d.Titles)
            .ThenInclude(t => t.Item)
            .AsAsyncEnumerable();

        await foreach (var item in boxsets)
        {
            string relativeUrl = $"/boxset/{item.Slug}";
            results.Add(CreateSitemapNode(siteBase, relativeUrl, priority: 1));

            if (item.Release == null)
            {
                continue;
            }

            foreach (var disc in item.Release.Discs)
            {
                relativeUrl = $"/boxset/{item.Slug}/discs/{disc.Index}";
                results.Add(CreateSitemapNode(siteBase, relativeUrl));

                foreach (var title in disc.Titles)
                {
                    if (title.Item != null)
                    {
                        relativeUrl = $"/boxset/{item.Slug}/discs/{disc.Index}/{GetTitleUrl(title)}";
                        results.Add(CreateSitemapNode(siteBase, relativeUrl));
                    }
                }
            }
        }
    }

    private static string GetTitleUrl(Title title)
    {
        if (string.IsNullOrEmpty(title.SourceFile))
        {
            return "";
        }

        return title.SourceFile.Replace(".", "/").Replace("(", "[").Replace(")", "]");
    }

    private static SitemapNode CreateSitemapNode(string siteBase, string relativeUrl, SitemapFrequency? frequency = null, double? priority = null, DateTime? lastModified = null)
    {
        return new SitemapNode
        {
            Url = siteBase + relativeUrl,
            Frequency = frequency,
            Priority = priority,
            LastModified = lastModified
        };
    }

}
