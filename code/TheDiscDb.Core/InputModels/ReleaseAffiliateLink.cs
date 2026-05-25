namespace TheDiscDb.InputModels
{
    using System;

    /// <summary>
    /// A mapping from a <see cref="Release"/> to an affiliate-program product on a third-party
    /// storefront (e.g. gruv.com). Joined to <see cref="Release"/> at query time on a slug pair —
    /// <c>(MediaItemSlug | BoxsetSlug, ReleaseSlug)</c> — NOT on <see cref="Release.Id"/>, because
    /// release IDs are not stable across database rebuilds from the <c>data/</c> JSON files.
    /// </summary>
    /// <remarks>
    /// Exactly one of <see cref="MediaItemSlug"/> or <see cref="BoxsetSlug"/> must be populated;
    /// a database CHECK constraint enforces this. The table is treated as DISPOSABLE: it is
    /// populated by an out-of-band tool (ContributionBuddy's <c>gruv-match</c> task) and can be
    /// regenerated after any rebuild — no contributor-facing data lives here.
    /// </remarks>
    public class ReleaseAffiliateLink
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public int Id { get; set; }

        /// <summary>
        /// Slug of the parent <see cref="MediaItem"/> when this affiliate link is for a Release
        /// owned by a MediaItem (movie / series). Mutually exclusive with <see cref="BoxsetSlug"/>.
        /// </summary>
        public string? MediaItemSlug { get; set; }

        /// <summary>
        /// Slug of the parent <see cref="Boxset"/> when this affiliate link is for a Release owned
        /// by a Boxset. Mutually exclusive with <see cref="MediaItemSlug"/>.
        /// </summary>
        public string? BoxsetSlug { get; set; }

        /// <summary>Slug of the <see cref="Release"/> this link points to.</summary>
        public string ReleaseSlug { get; set; } = string.Empty;

        /// <summary>Affiliate provider identifier — currently always <c>"gruv"</c>.</summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>
        /// Provider's product handle / slug. For gruv.com this is the Shopify handle
        /// (e.g. <c>jaws-4k-uhd-blu-ray</c>) used to construct the canonical URL.
        /// </summary>
        public string ProviderHandle { get; set; } = string.Empty;

        /// <summary>
        /// Cached canonical product URL on the provider's site. Cached so a future change to the
        /// provider's URL shape doesn't strand existing rows that were matched against the prior
        /// shape — we keep the URL we matched against.
        /// </summary>
        public string ProviderUrl { get; set; } = string.Empty;

        /// <summary>
        /// UPC used to make the match (audit trail). Null when the match wasn't UPC-based
        /// (e.g. manual or title-fuzzy).
        /// </summary>
        public string? MatchedUpc { get; set; }

        /// <summary>
        /// How this row was matched: <c>upc-exact</c>, <c>manual</c>, or <c>title-fuzzy</c>.
        /// </summary>
        public string MatchSource { get; set; } = string.Empty;

        public DateTimeOffset MatchedAt { get; set; }

        public string? Notes { get; set; }
    }
}
