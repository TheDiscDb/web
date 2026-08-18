using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using ScrapySharp.Extensions;
using ScrapySharp.Network;
using TheDiscDb.Data.Import;

namespace TheDiscDb.Services;

public class AmazonImportException : Exception
{
    public AmazonImportException(string message) : base(message)
    {
    }

    public AmazonImportException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class AmazonImporter : IAmazonImporter
{
    private readonly ScrapingBrowser browser;
    private readonly IStaticAssetStore assets;

    public AmazonImporter(IStaticAssetStore assets)
    {
        this.browser = new ScrapingBrowser();
        this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    private async Task SaveResponse(string response, string remotePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using MemoryStream stream = new MemoryStream();
            using (StreamWriter writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(response);
                await this.assets.Save(stream, remotePath, ContentTypes.TextContentType, cancellationToken);
            }
        }
        catch (Exception)
        {
        }
    }

    public async Task<AmazonProductMetadata?> GetProductMetadataAsync(string asin, CancellationToken cancellationToken = default)
    {
        string normalizedAsin = asin.Trim().ToUpperInvariant();
        try
        {
            AmazonProductMetadata? cached = await GetCachedMetadataAsync(normalizedAsin, cancellationToken);
            if (cached is not null)
            {
                cached.MediaTitle ??= ExtractMediaTitle(cached.Title);
                return cached;
            }

            AmazonProductMetadata result = new AmazonProductMetadata();
            WebPage html = await browser.NavigateToPageAsync(GetUrl(normalizedAsin));

            if (html.RawResponse.StatusCode < 200 || html.RawResponse.StatusCode > 299)
            {
                throw new AmazonImportException($"Failed to retrieve Amazon page for ASIN {normalizedAsin}. Status code: {html.RawResponse.StatusCode}");
            }

            if (string.IsNullOrEmpty(html.Content))
            {
                throw new AmazonImportException($"Could not retrieve Amazon page for ASIN {normalizedAsin}. Empty content.");
            }

            var nodes = html.Html.CssSelect("div#detailBullets_feature_div");
            var node = nodes.FirstOrDefault();
            if (node != null)
            {
                var listItems = node.Descendants()
                    .Where(n => n.Name == "li")
                    .ToList();

                var details = ParseDetails(listItems);
                result = BuildMetadata(details);
            }
            else
            {
                string logPath = $"logs/{normalizedAsin}-{Guid.NewGuid()}.html";
                await SaveResponse(html.RawResponse.ToString(), logPath, cancellationToken);
                throw new AmazonImportException("Could not find detail bullets on Amazon page. " + logPath);
            }

            result.Asin ??= normalizedAsin;
            result.Title = GetProductTitle(html) ?? result.Title;
            result.MediaTitle = ExtractMediaTitle(result.Title);

            var imageData = GetImageData(html.RawResponse.ToString());

            if (imageData == null)
            {
                string logPath = $"logs/{normalizedAsin}-{Guid.NewGuid()}.html";
                await SaveResponse(html.RawResponse.ToString(), logPath, cancellationToken);
                throw new AmazonImportException("Could not find image data on Amazon page. " + logPath);
            }

            var front = imageData.Initial
                .FirstOrDefault(i => i.Variant!.Equals("FRNT", StringComparison.OrdinalIgnoreCase));
            if (front == null)
            {
                front = imageData.Initial
                    .FirstOrDefault(i => i.Variant!.Equals("MAIN", StringComparison.OrdinalIgnoreCase));
            }

            if (front != null)
            {
                result.FrontImageUrl = front.HiRes;
            }

            var back = imageData.Initial
                .FirstOrDefault(i => i.Variant!.Equals("BACK", StringComparison.OrdinalIgnoreCase));
            if (back != null)
            {
                result.BackImageUrl = back.HiRes;
            }

            await SaveCachedMetadataAsync(normalizedAsin, result, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            throw new AmazonImportException($"An error occurred while retrieving Amazon product metadata for ASIN {normalizedAsin}: {ex.Message}", ex);
        }
    }

    private async Task<AmazonProductMetadata?> GetCachedMetadataAsync(
        string asin,
        CancellationToken cancellationToken)
    {
        string cachePath = GetCachePath(asin);
        if (!await this.assets.Exists(cachePath, cancellationToken))
        {
            return null;
        }

        BinaryData data = await this.assets.Download(cachePath, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<AmazonProductMetadata>(data);
        }
        catch (JsonException)
        {
            await this.assets.Delete(cachePath, cancellationToken);
            return null;
        }
    }

    private async Task SaveCachedMetadataAsync(
        string asin,
        AmazonProductMetadata metadata,
        CancellationToken cancellationToken)
    {
        byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata));
        using var stream = new MemoryStream(json);
        await this.assets.Save(
            stream,
            GetCachePath(asin),
            ContentTypes.JsonContentType,
            cancellationToken);
    }

    private static string GetCachePath(string asin) => $"cache/amazon/{asin}.json";

    private static string? GetProductTitle(WebPage html)
    {
        HtmlNode? title = html.Html.CssSelect("#productTitle").FirstOrDefault();
        if (title is null)
        {
            return null;
        }

        string value = System.Net.WebUtility.HtmlDecode(title.InnerText);
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractMediaTitle(string? productTitle)
    {
        if (string.IsNullOrWhiteSpace(productTitle))
        {
            return null;
        }

        string title = Regex.Replace(productTitle, @"\s+", " ").Trim();
        foreach (string separator in new[] { " - ", " \u2013 ", " \u2014 " })
        {
            int separatorIndex = title.LastIndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex > 0 &&
                IsFormatDescription(title[(separatorIndex + separator.Length)..]))
            {
                return title[..separatorIndex].Trim();
            }
        }

        title = Regex.Replace(
            title,
            @"\s*[\(\[][^)\]]*\b(?:4K|UHD|Ultra HD|Blu-?ray|DVD|Digital|Steelbook)\b[^)\]]*[\)\]]\s*$",
            string.Empty,
            RegexOptions.IgnoreCase);
        title = Regex.Replace(
            title,
            @"\s+(?:4K(?:\s+(?:Ultra\s+HD|UHD))?|UHD|Ultra\s+HD|Blu-?ray|DVD)(?:\s*\+\s*(?:Blu-?ray|DVD|Digital))*\s*$",
            string.Empty,
            RegexOptions.IgnoreCase);
        return title.Trim().TrimEnd('-', '\u2013', '\u2014', ':').Trim();
    }

    private static bool IsFormatDescription(string value) =>
        Regex.IsMatch(
            value,
            @"\b(?:4K|UHD|Ultra HD|Blu-?ray|DVD|Digital|Steelbook)\b",
            RegexOptions.IgnoreCase);

    private AmazonColorImages? GetImageData(string html)
    {
        string json = Regex.Match(html, @"var data = \{\s+'colorImages'\:\s+(.*)")
            .Groups[1].Value
            .TrimEnd(",")
            .ToString()
            .Replace("'initial'", "\"initial\"");

        if (string.IsNullOrEmpty(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<AmazonColorImages>(json)!;
    }

    private AmazonProductMetadata BuildMetadata(Dictionary<string, string> details)
    {
        var metadata = new AmazonProductMetadata();
        if (details.TryGetValue("ASIN", out string? asin))
        {
            metadata.Asin = asin;
        }
        if (details.TryGetValue("UPC", out string? upc))
        {
            metadata.Upc = Regex.Replace(upc, @"\D", "");
        }
        if (details.TryGetValue("Aspect Ratio", out var aspectRatio))
        {
            metadata.AspectRatio = aspectRatio;
        }
        if (details.TryGetValue("Release date", out var releaseDateStr) &&
            DateTimeOffset.TryParse(releaseDateStr, out var releaseDate))
        {
            metadata.ReleaseDate = releaseDate;
        }
        if (details.TryGetValue("Number of discs", out var numberOfDiscsStr) &&
            int.TryParse(numberOfDiscsStr, out var numberOfDiscs))
        {
            metadata.NumberOfDiscs = numberOfDiscs;
        }
        if (details.TryGetValue("Is Discontinued By Manufacturer", out var discontinuedStr))
        {
            metadata.IsDiscontinued = discontinuedStr.Equals("Yes", StringComparison.OrdinalIgnoreCase);
        }
        if (details.TryGetValue("MPAA rating", out var mpaaRating))
        {
            metadata.MpaaRating = mpaaRating;
        }
        if (details.TryGetValue("Item model number", out var modelNumber))
        {
            metadata.ModelNumber = modelNumber;
        }
        if (details.TryGetValue("Director", out var director))
        {
            metadata.Director = director;
        }
        if (details.TryGetValue("Media Format", out var mediaFormat))
        {
            metadata.MediaFormat = mediaFormat;
        }
        if (details.TryGetValue("Actors", out var actors))
        {
            metadata.Actors = actors;
        }
        if (details.TryGetValue("Producers", out var producers))
        {
            metadata.Producers = producers;
        }
        if (details.TryGetValue("Language", out var language))
        {
            metadata.Language = language;
        }
        if (details.TryGetValue("Dubbed", out var dubbed))
        {
            metadata.Dubbed = dubbed;
        }
        if (details.TryGetValue("Subtitles", out var subtitles))
        {
            metadata.Subtitles = subtitles;
        }
        if (details.TryGetValue("Studio", out var studio))
        {
            metadata.Studio = studio;
        }

        return metadata;
    }

    private Dictionary<string, string> ParseDetails(IEnumerable<HtmlNode> listItem)
    {
        Dictionary<string, string> details = new(StringComparer.OrdinalIgnoreCase);

        foreach (var item in listItem)
        {
            var children = item.Descendants()
                .First(n => n.Name == "span")
                .Descendants()
                .Where(n => n.Name == "span")
                .ToList();
            if (children.Count >= 2)
            {
                var key = children[0].InnerText.Trim();

                // Normalize whitespace and remove HTML entities
                key = System.Net.WebUtility.HtmlDecode(key);
                key = System.Text.RegularExpressions.Regex.Replace(key, @"\s+", " ")
                    .Replace(" : ", "")
                    .Trim();

                // remove control characters
                key = Regex.Replace(key, @"[\p{C}\p{Cf}\u200B-\u200F]+", "")
                    .Trim();

                var value = children[1].InnerText.Trim();
                details[key] = value;
            }
        }

        return details;
    }

    private static Uri GetUrl(string asin)
    {
        return new Uri($"https://www.amazon.com/dp/{asin}/");
    }
}

public class AmazonColorImages
{
    [JsonPropertyName("initial")]
    public List<AmazonImageVariant> Initial { get; set; } = new();
}

public class AmazonImageVariant
{
    [JsonPropertyName("hiRes")]
    public string? HiRes { get; set; }

    [JsonPropertyName("thumb")]
    public string? Thumb { get; set; }

    [JsonPropertyName("large")]
    public string? Large { get; set; }

    [JsonPropertyName("main")]
    public Dictionary<string, List<int>>? Main { get; set; }

    [JsonPropertyName("variant")]
    public string? Variant { get; set; }

    [JsonPropertyName("lowRes")]
    public string? LowRes { get; set; }

    [JsonPropertyName("shoppableScene")]
    public object? ShoppableScene { get; set; }

    [JsonPropertyName("feedbackMetadata")]
    public object? FeedbackMetadata { get; set; }
}
