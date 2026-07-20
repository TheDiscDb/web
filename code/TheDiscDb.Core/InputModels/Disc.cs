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
        /// Transient carrier for the pressing's globally-stable Disc ID as read from a
        /// <c>disc*.json</c> (or overridden from a <c>.ref</c>). This is NOT a column on the shared
        /// canonical disc: the id identifies a physical pressing, so import copies it onto the
        /// owning <see cref="ReleaseDisc.GlobalDiscId"/> (the same pattern as
        /// <see cref="Index"/>/<see cref="Slug"/>/<see cref="Name"/>). Serialized under the
        /// PascalCase <c>GlobalDiscId</c> key like the rest of <c>disc*.json</c> (no naming policy),
        /// so it needs no <c>JsonPropertyName</c>.
        /// </summary>
        [NotMapped]
        [HotChocolate.GraphQLIgnore]
        public string? GlobalDiscId { get; set; }

        [HotChocolate.Data.UseFiltering]
        [HotChocolate.Data.UseSorting]
        public ICollection<Title> Titles { get; set; } = new HashSet<Title>();
        [System.Text.Json.Serialization.JsonIgnore, NotMapped]
        public Release? Release { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<ReleaseDisc> ReleaseDiscs { get; set; } = new HashSet<ReleaseDisc>();
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
