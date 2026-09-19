namespace TheDiscDb.Components.Controls;

using Markdig;
using Microsoft.AspNetCore.Components;
using System.Net;
using System.Text.RegularExpressions;

public partial class MarkdownContent : ComponentBase
{
    [Parameter]
    public string? Content { get; set; }

    private string RenderedHtml => Render(Content);

    internal static string Render(string? content) => string.IsNullOrEmpty(content)
        ? string.Empty
        : SafeUrlAttribute().Replace(
            Markdown.ToHtml(content, Pipeline),
            match => IsSafeUrl(match.Groups["name"].Value, WebUtility.HtmlDecode(match.Groups["url"].Value))
                ? match.Value
                : string.Empty);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAdvancedExtensions()
        .Build();

    private static bool IsSafeUrl(string attributeName, string url)
    {
        if (url.StartsWith("/", StringComparison.Ordinal) ||
            url.StartsWith("#", StringComparison.Ordinal))
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https" ||
            (attributeName.Equals("href", StringComparison.OrdinalIgnoreCase) &&
             uri.Scheme == "mailto");
    }

    [GeneratedRegex(@"(?<name>href|src)=""(?<url>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex SafeUrlAttribute();
}
