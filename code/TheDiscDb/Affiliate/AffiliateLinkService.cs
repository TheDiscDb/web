namespace TheDiscDb.Affiliate;

using Microsoft.Extensions.Options;

/// <summary>
/// Decorates outbound gruv.com URLs with CJ (Commission Junction) deep-link tracking and UTM
/// tags. All user-facing links to gruv.com — site UI, footer credit — MUST go through
/// <see cref="Decorate(string?, string?)"/>. Raw <c>ReleaseAffiliateLink.ProviderUrl</c> is reserved
/// for storage and must never be rendered as an anchor href.
/// </summary>
/// <remarks>
/// CJ deep-link format (the format CJ's own deep-link generator emits for the GRUV program):
/// <code>
/// https://{CjTrackingDomain}/click-{Pid}-{AdvertiserId}?url={urlEncodedDestination}&amp;sid={sid}
/// </code>
/// UTM parameters are merged onto the destination URL BEFORE URL-encoding it into the <c>url=</c>
/// query parameter, so Google Analytics on gruv.com receives attribution alongside CJ commission
/// tracking. The <c>sid</c> query parameter is omitted entirely when no sub-id is supplied.
/// If either <see cref="AffiliateLinkOptions.Pid"/> or <see cref="AffiliateLinkOptions.AdvertiserId"/>
/// is null/empty, this service falls back to returning the destination URL with UTM tags only
/// (no CJ redirect). This keeps dev environments without affiliate IDs functional.
/// <para>
/// Ported from GruvWishlist.Core.Affiliate.AffiliateLinkService. Kept as a copy rather than a
/// shared package to avoid a cross-repo NuGet dependency for ~100 lines of code.
/// </para>
/// </remarks>
public class AffiliateLinkService(IOptions<AffiliateLinkOptions> options)
{
    private readonly AffiliateLinkOptions options = options.Value;

    /// <summary>Decorates the URL with no sub-id (CJ <c>sid</c> query param will be omitted).</summary>
    public string Decorate(string? originalUrl) => this.Decorate(originalUrl, sid: null);

    /// <summary>
    /// Decorates the URL with the supplied sub-id. The <paramref name="sid"/> appears in CJ
    /// reports and lets us see which surface (e.g. <c>release-detail</c>, <c>release-list</c>,
    /// <c>boxset-detail</c>) drove each click.
    /// </summary>
    /// <remarks>
    /// SECURITY: this method enforces a strict scheme + host allowlist. Only
    /// <c>https://gruv.com</c> and <c>https://www.gruv.com</c> URLs are decorated; anything else
    /// (including <c>javascript:</c>, <c>data:</c>, <c>http:</c>, or other domains) returns
    /// <see cref="string.Empty"/>. Callers MUST treat an empty return as "do not render an
    /// anchor", which is the contract <c>GruvBuyButton</c> relies on.
    /// </remarks>
    public string Decorate(string? originalUrl, string? sid)
    {
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            return string.Empty;
        }
        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }
        if (!IsAllowedGruvUrl(uri))
        {
            return string.Empty;
        }

        var withUtm = AppendUtm(originalUrl, uri);

        if (string.IsNullOrWhiteSpace(this.options.Pid)
            || string.IsNullOrWhiteSpace(this.options.AdvertiserId))
        {
            return withUtm;
        }

        var trackingDomain = string.IsNullOrWhiteSpace(this.options.CjTrackingDomain)
            ? "www.anrdoezrs.net"
            : this.options.CjTrackingDomain.Trim();

        var pid = Uri.EscapeDataString(this.options.Pid.Trim());
        var aid = Uri.EscapeDataString(this.options.AdvertiserId.Trim());
        var encodedDestination = Uri.EscapeDataString(withUtm);

        var url = $"https://{trackingDomain}/click-{pid}-{aid}?url={encodedDestination}";

        if (!string.IsNullOrWhiteSpace(sid))
        {
            url += $"&sid={Uri.EscapeDataString(sid.Trim())}";
        }

        return url;
    }

    private static bool IsAllowedGruvUrl(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var host = uri.Host;
        return string.Equals(host, "gruv.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "www.gruv.com", StringComparison.OrdinalIgnoreCase);
    }

    private string AppendUtm(string originalUrl, Uri uri)
    {
        var utmParts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(this.options.UtmSource))
            utmParts.Add($"utm_source={Uri.EscapeDataString(this.options.UtmSource)}");
        if (!string.IsNullOrWhiteSpace(this.options.UtmMedium))
            utmParts.Add($"utm_medium={Uri.EscapeDataString(this.options.UtmMedium)}");
        if (!string.IsNullOrWhiteSpace(this.options.UtmCampaign))
            utmParts.Add($"utm_campaign={Uri.EscapeDataString(this.options.UtmCampaign)}");

        if (utmParts.Count == 0)
        {
            return originalUrl;
        }

        // Split off any fragment so UTMs land in the query string, not inside `#…`. Using
        // string-splitting (rather than UriBuilder) preserves the original URL's exact form —
        // UriBuilder would otherwise normalize default ports, casing, etc.
        var hashIdx = originalUrl.IndexOf('#');
        var beforeHash = hashIdx >= 0 ? originalUrl[..hashIdx] : originalUrl;
        var fragment = hashIdx >= 0 ? originalUrl[hashIdx..] : string.Empty;

        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return $"{beforeHash}{separator}{string.Join('&', utmParts)}{fragment}";
    }
}
