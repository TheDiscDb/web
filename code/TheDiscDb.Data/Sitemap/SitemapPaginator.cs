namespace TheDiscDb.Web.Sitemap;

using System;
using System.Collections.Generic;
using System.Linq;

// A single page of a (possibly paginated) category sitemap. "Stem" is the file name
// (without the "sitemap-" prefix / ".xml" suffix) that the page is served under, e.g.
// "movies-discs" for page 1, "movies-discs-2" for page 2, etc.
public record SitemapPage(string Stem, IReadOnlyList<SitemapNode> Nodes);

public static class SitemapPaginator
{
    // Google's documented limit is 50,000 URLs per sitemap file.
    public const int DefaultPageSize = 50_000;

    public static IReadOnlyList<SitemapPage> Paginate(string category, IReadOnlyList<SitemapNode> nodes, int pageSize = DefaultPageSize)
    {
        if (string.IsNullOrEmpty(category))
        {
            throw new ArgumentException("Category must not be null or empty.", nameof(category));
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be greater than zero.");
        }

        if (nodes.Count == 0)
        {
            // Still emit a single (empty) page so the category has a stable entry in the
            // sitemap index even when there's currently no content for it.
            return new[] { new SitemapPage(category, Array.Empty<SitemapNode>()) };
        }

        var pages = new List<SitemapPage>();
        int pageNumber = 1;
        for (int offset = 0; offset < nodes.Count; offset += pageSize)
        {
            var slice = nodes.Skip(offset).Take(pageSize).ToList();
            string stem = pageNumber == 1 ? category : $"{category}-{pageNumber}";
            pages.Add(new SitemapPage(stem, slice));
            pageNumber++;
        }

        return pages;
    }
}
