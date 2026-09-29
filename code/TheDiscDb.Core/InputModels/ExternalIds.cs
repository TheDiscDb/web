namespace TheDiscDb.InputModels
{
    public class ExternalIds
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public int Id { get; set; }
        public string? Tmdb { get; set; }
        public string? Imdb { get; set; }
        public string? Tvdb { get; set; }

        /// <summary>
        /// Numeric-id-only provider identifiers keyed by <see cref="ExternalProviderCatalog"/>
        /// provider id (e.g. <c>"bluray"</c>, <c>"dvdcompare"</c>, <c>"dvdtalk"</c>). Values are
        /// substituted into the provider's static URL template to build a link — no slugs or
        /// full URLs are stored, only the id.
        /// </summary>
        public string? BlurayCom { get; set; }
        public string? DvdCompare { get; set; }
        public string? DvdTalk { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public MediaItem? MediaItem { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Release? Release { get; set; }
    }
}
