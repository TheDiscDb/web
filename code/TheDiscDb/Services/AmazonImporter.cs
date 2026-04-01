using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
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
    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36",
    ];

    private readonly HttpClient httpClient;
    private readonly IStaticAssetStore assets;
    private readonly ILogger<AmazonImporter> logger;

    public AmazonImporter(IStaticAssetStore assets, ILogger<AmazonImporter> logger)
    {
        this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };

        this.httpClient = new HttpClient(handler);
    }

    private HttpRequestMessage CreateRequest(Uri url)
    {
        string userAgent = UserAgents[Random.Shared.Next(UserAgents.Length)];

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

        return request;
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
        try
        {
            var url = GetUrl(asin);
            using var request = CreateRequest(url);
            using var response = await this.httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new AmazonImportException($"Failed to retrieve Amazon page for ASIN {asin}. Status code: {(int)response.StatusCode}");
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrEmpty(content))
            {
                throw new AmazonImportException($"Could not retrieve Amazon page for ASIN {asin}. Empty content.");
            }

            // Detect bot blocking
            if (content.Contains("To discuss automated access to Amazon data", StringComparison.OrdinalIgnoreCase)
                || content.Contains("api-services-support@amazon.com", StringComparison.OrdinalIgnoreCase))
            {
                this.logger.LogWarning("Amazon bot detection triggered for ASIN {Asin}", asin);
                throw new AmazonImportException($"Amazon blocked the request for ASIN {asin}. Bot detection triggered.");
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            AmazonProductMetadata result;
            var node = doc.GetElementbyId("detailBullets_feature_div");
            if (node != null)
            {
                var listItems = node.Descendants("li").ToList();
                var details = ParseDetails(listItems);
                result = BuildMetadata(details);
            }
            else
            {
                string logPath = $"logs/{asin}-{Guid.NewGuid()}.html";
                await SaveResponse(content, logPath, cancellationToken);
                throw new AmazonImportException("Could not find detail bullets on Amazon page. " + logPath);
            }

            var imageData = GetImageData(content);

            if (imageData == null)
            {
                string logPath = $"logs/{asin}-{Guid.NewGuid()}.html";
                await SaveResponse(content, logPath, cancellationToken);
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

            return result;
        }
        catch (AmazonImportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AmazonImportException($"An error occurred while retrieving Amazon product metadata for ASIN {asin}: {ex.Message}", ex);
        }
    }

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
            var firstSpan = item.Descendants("span").FirstOrDefault();
            if (firstSpan == null)
            {
                continue;
            }

            var children = firstSpan.Descendants("span").ToList();
            if (children.Count >= 2)
            {
                var key = children[0].InnerText.Trim();

                // Normalize whitespace and remove HTML entities
                key = System.Net.WebUtility.HtmlDecode(key);
                key = Regex.Replace(key, @"\s+", " ")
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

