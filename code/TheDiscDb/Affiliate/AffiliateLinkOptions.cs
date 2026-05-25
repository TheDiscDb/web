namespace TheDiscDb.Affiliate;

/// <summary>
/// Options for <see cref="AffiliateLinkService"/>, bound from the <c>Gruv:</c> configuration
/// section. Both <see cref="Pid"/> and <see cref="AdvertiserId"/> are needed for CJ deep-linking;
/// without them the service degrades to UTM-tagged URLs only (no CJ redirect, no commission).
/// </summary>
/// <remarks>
/// The <see cref="Pid"/> and <see cref="AdvertiserId"/> are the two integers in the
/// <c>click-PID-AID</c> path segment of any CJ deep-link generated for the GRUV affiliate program.
/// They are public attribution tokens (embedded in every outbound URL), not credentials.
/// thediscdb.com reuses GruvWishlist.com's IDs for V1 — see the integration plan.
/// </remarks>
public class AffiliateLinkOptions
{
    public string? Pid { get; set; }
    public string? AdvertiserId { get; set; }
    public string? CjTrackingDomain { get; set; }
    public string? UtmSource { get; set; } = "thediscdb";
    public string? UtmMedium { get; set; } = "referral";
    public string? UtmCampaign { get; set; } = "release";
}
