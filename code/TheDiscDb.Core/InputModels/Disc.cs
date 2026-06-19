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
