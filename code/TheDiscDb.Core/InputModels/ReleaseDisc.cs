namespace TheDiscDb.InputModels;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

public class ReleaseDisc : IDisc
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int Id { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int ReleaseId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Release? Release { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int DiscId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Disc? Disc { get; set; }

    public int Index { get; set; }
    public string? Slug { get; set; }
    public string? Name { get; set; }

    [NotMapped]
    public string? Format
    {
        get => this.Disc?.Format;
        set
        {
            if (this.Disc != null)
            {
                this.Disc.Format = value;
            }
        }
    }

    [NotMapped]
    public string? ContentHash
    {
        get => this.Disc?.ContentHash;
        set
        {
            if (this.Disc != null)
            {
                this.Disc.ContentHash = value;
            }
        }
    }

    [NotMapped]
    public string? GlobalDiscId
    {
        get => this.Disc?.GlobalDiscId;
        set
        {
            if (this.Disc != null)
            {
                this.Disc.GlobalDiscId = value;
            }
        }
    }

    [HotChocolate.Data.UseFiltering]
    [HotChocolate.Data.UseSorting]
    [NotMapped]
    public ICollection<Title> Titles => this.Disc?.Titles ?? Array.Empty<Title>();
}
