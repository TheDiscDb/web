namespace TheDiscDb.InputModels
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A single static, code-defined third-party catalog/review site that a <see cref="Release"/>
    /// (or <see cref="MediaItem"/>) can be linked to (see TheDiscDb/web#82). The catalog is
    /// intentionally NOT admin/DB-editable — adding a provider requires a code change + deploy —
    /// and only an id (no slug, no raw URL) is ever stored per <see cref="ExternalIds"/> row.
    /// </summary>
    /// <param name="Id">
    /// Stable key matching an <see cref="ExternalIds"/> property (e.g. <c>"bluray"</c>). Used as
    /// the field name in the contribution UI and must never change once shipped, or previously
    /// stored ids become orphaned.
    /// </param>
    /// <param name="Name">Human-readable display name, e.g. "Blu-ray.com".</param>
    /// <param name="UrlTemplate">
    /// URL template with a single <c>{0}</c> placeholder for the stored id.
    /// </param>
    /// <param name="LogoUrl">Site-relative path to the provider's logo, served from wwwroot.</param>
    public record ExternalProviderDefinition(string Id, string Name, string UrlTemplate, string LogoUrl)
    {
        /// <summary>Builds the outbound link for the given stored id, or null if id is empty.</summary>
        public string? BuildUrl(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return string.Format(this.UrlTemplate, id.Trim());
        }
    }

    /// <summary>
    /// Static, site-wide catalog of supported third-party providers. See
    /// <see cref="ExternalProviderDefinition"/> for why this is code-only rather than DB-driven.
    /// </summary>
    public static class ExternalProviderCatalog
    {
        /// <summary>
        /// Providers keyed by <see cref="ExternalProviderDefinition.Id"/>, in display order.
        /// </summary>
        public static readonly IReadOnlyList<ExternalProviderDefinition> Providers = new List<ExternalProviderDefinition>
        {
            // blu-ray.com's canonical URLs embed the title slug (e.g. /movies/Jaws-Blu-ray/1234/),
            // but the site also serves a shortlink that redirects using only the numeric id:
            // https://www.blu-ray.com/movies/movies.php?id={id}. That's what we store/link to,
            // since we intentionally keep only an id (no slug) per TheDiscDb/web#82. Note this
            // shortlink always resolves to the "movies" (Blu-ray/4K) section, not the DVD section.
            new("bluray", "Blu-ray.com", "https://www.blu-ray.com/movies/movies.php?id={0}", "/providers/bluray-com-icon.png"),
            new("dvdcompare", "DVDCompare", "https://dvdcompare.net/comparisons/film.php?fid={0}", "/providers/dvdcompare-icon.png"),
            new("dvdtalk", "DVD Talk", "https://www.dvdtalk.com/reviews/read/{0}", "/providers/dvdtalk-icon.png"),
        };

        /// <summary>Looks up a provider definition by id, or null if unknown.</summary>
        public static ExternalProviderDefinition? Find(string id) =>
            Providers.FirstOrDefault(p => p.Id.Equals(id, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>Gets the stored id for a given provider on an <see cref="ExternalIds"/> row.</summary>
        public static string? GetStoredId(ExternalIds externalIds, string providerId) => providerId switch
        {
            "bluray" => externalIds.BlurayCom,
            "dvdcompare" => externalIds.DvdCompare,
            "dvdtalk" => externalIds.DvdTalk,
            _ => null,
        };

        /// <summary>Sets the stored id for a given provider on an <see cref="ExternalIds"/> row.</summary>
        public static void SetStoredId(ExternalIds externalIds, string providerId, string? value)
        {
            switch (providerId)
            {
                case "bluray":
                    externalIds.BlurayCom = value;
                    break;
                case "dvdcompare":
                    externalIds.DvdCompare = value;
                    break;
                case "dvdtalk":
                    externalIds.DvdTalk = value;
                    break;
            }
        }
    }
}
