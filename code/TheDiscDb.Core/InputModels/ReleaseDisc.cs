namespace TheDiscDb.InputModels;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

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

    /// <summary>
    /// This pressing's globally-stable Disc ID (AACS Disc ID for Blu-ray/UHD; libdvdread DVDDiscID
    /// for DVD). The id identifies a physical pressing, so it lives on the release-disc rather than
    /// the shared canonical <see cref="Disc"/>: two releases whose discs share a content hash each
    /// carry their own id here. Globally unique across release-discs; add-only; optional (absent on
    /// MKV-only rips).
    /// </summary>
    public string? GlobalDiscId { get; set; }

    [HotChocolate.Data.UseFiltering]
    [HotChocolate.Data.UseSorting]
    [NotMapped]
    public ICollection<Title> Titles => this.Disc?.Titles ?? Array.Empty<Title>();
}

public static class ReleaseDiscExtensions
{
    /// <summary>
    /// The pressing's <b>effective</b> Disc ID: its own stored <see cref="ReleaseDisc.GlobalDiscId"/>
    /// when present; otherwise the single distinct id shared by the other release-discs of the same
    /// canonical <see cref="Disc"/> (which represent the same content, so usually the same pressing).
    /// Returns <c>null</c> when it has no own id and its siblings either have none or disagree — a
    /// genuine re-press collision where this pressing's id is unknown. The sibling fallback requires
    /// <see cref="Disc.ReleaseDiscs"/> to be loaded; when it isn't, only the own id is considered.
    /// </summary>
    public static string? EffectiveGlobalDiscId(this ReleaseDisc releaseDisc)
    {
        if (releaseDisc is null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(releaseDisc.GlobalDiscId))
        {
            return releaseDisc.GlobalDiscId;
        }

        var siblings = releaseDisc.Disc?.ReleaseDiscs;
        if (siblings is null)
        {
            return null;
        }

        return EffectiveGlobalDiscId(siblings.Select(s => s.GlobalDiscId));
    }

    /// <summary>
    /// Computes the unambiguous id from a set of release-disc ids (typically the ids of every
    /// release-disc sharing one canonical disc): the single distinct non-empty value, or <c>null</c>
    /// when there are none or more than one (a collision).
    /// </summary>
    public static string? EffectiveGlobalDiscId(IEnumerable<string?> candidateIds)
    {
        string? found = null;
        foreach (var id in candidateIds)
        {
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (found is null)
            {
                found = id;
            }
            else if (!string.Equals(found, id, StringComparison.OrdinalIgnoreCase))
            {
                return null; // siblings disagree — ambiguous
            }
        }

        return found;
    }
}
