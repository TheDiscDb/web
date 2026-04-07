namespace TheDiscDb.Web.Data;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using HotChocolate;

public class UserContributionDiscItem : IHasId
{
    [JsonIgnore]
    public int Id { get; set; }
    [NotMapped]
    [GraphQLIgnore]
    public string EncodedId { get; set; } = default!;
    [JsonIgnore]
    public UserContributionDisc Disc { get; set; } = default!;
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int ChapterCount { get; set; } = 0;
    public int SegmentCount { get; set; } = 0;
    public string SegmentMap { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    public string? Season { get; set; } = string.Empty;
    public string? Episode { get; set; } = string.Empty;

    public ICollection<UserContributionChapter> Chapters { get; set; } = new HashSet<UserContributionChapter>();
    public ICollection<UserContributionAudioTrack> AudioTracks { get; set; } = new HashSet<UserContributionAudioTrack>();
}
