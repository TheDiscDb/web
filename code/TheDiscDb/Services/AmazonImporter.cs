using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ScrapySharp.Extensions;
using ScrapySharp.Network;

namespace TheDiscDb.Services;

public class AmazonImporter : IAmazonImporter
{
    private readonly ScrapingBrowser browser;

    public AmazonImporter()
    {
        this.browser = new ScrapingBrowser();
    }

    public Task<AmazonProductMetadata?> GetProductMetadataAsync(string asin, CancellationToken cancellationToken = default)
    {
        AmazonProductMetadata result = new AmazonProductMetadata();
        WebPage html = browser.NavigateToPage(GetUrl(asin));
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

        var imageData = GetImageData(html.RawResponse.ToString());
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

        return Task.FromResult<AmazonProductMetadata?>(result);
    }

    private AmazonColorImages GetImageData(string html)
    {
        string json = Regex.Match(html, @"var data = \{\s+'colorImages'\:\s+(.*)")
            .Groups[1].Value
            .TrimEnd(",")
            .ToString()
            .Replace("'initial'", "\"initial\"");
        return JsonSerializer.Deserialize<AmazonColorImages>(json)!;
    }

    private AmazonProductMetadata BuildMetadata(Dictionary<string, string> details)
    {
        var metadata = new AmazonProductMetadata();
        if (details.TryGetValue("ASIN", out string? asin))
        {
            metadata.Asin = asin;
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

