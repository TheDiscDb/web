using TheDiscDb.InputModels;

namespace TheDiscDb.InputModels
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations.Schema;

    public interface IDisc
    {
        int Index { get; }
        string? Slug { get; }
        string? Name { get; }
        string? Format { get; }
    }

    public class Disc : IDisc
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public int Id { get; set; }
        [NotMapped]
        public int Index { get; set; }
        [NotMapped]
        public string? Slug { get; set; }
        [NotMapped]
        public string? Name { get; set; }
        public string? Format { get; set; }
        public string? ContentHash { get; set; }

        /// <summary>
        /// Globally-stable, per-pressing disc identifier. For Blu-ray/UHD this is the AACS
        /// Disc ID (SHA-1 of AACS/Unit_Key_RO.inf); for DVD it is the libdvdread DVDDiscID
        /// (MD5 of the IFO files). The disc <see cref="Format"/> disambiguates which algorithm
        /// produced it. Add-only and optional (absent on MKV-only rips).
        /// </summary>
        public string? GlobalDiscId { get; set; }

        [HotChocolate.Data.UseFiltering]
        [HotChocolate.Data.UseSorting]
        public ICollection<Title> Titles { get; set; } = new HashSet<Title>();
        [System.Text.Json.Serialization.JsonIgnore, NotMapped]
        public Release? Release { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<ReleaseDisc> ReleaseDiscs { get; set; } = new HashSet<ReleaseDisc>();
        public bool IsPartial { get; set; }

        /// <summary>
        /// Marks this disc as a placeholder for a disc that is known to belong to a release
        /// but has not yet been contributed (no logs, no summary, no titles). Placeholders
        /// carry only <see cref="Name"/>, <see cref="Slug"/>, and <see cref="Format"/> and are
        /// release-specific: they are excluded from canonical disc dedup and have a null
        /// <see cref="ContentHash"/>. A release is considered partial when it contains at least
        /// one placeholder disc. Persisted in <c>/data</c> as <c>discNN.placeholder.json</c>.
        /// </summary>
        public bool IsPlaceholder { get; set; }
    }
}

public static class DiscExtensions
{
    public static string SlugOrIndex(this IDisc disc)
    {
        if (!string.IsNullOrEmpty(disc.Slug))
        {
            return disc.Slug;
        }

        return disc.Index.ToString();
    }
}
