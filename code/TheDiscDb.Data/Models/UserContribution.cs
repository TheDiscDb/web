namespace TheDiscDb.Web.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using HotChocolate;

public class UserContribution : IHasId, IContributionDisplay
{
    [JsonIgnore]
    public int Id { get; set; }
    [NotMapped]
    [GraphQLIgnore]
    public string EncodedId { get; set; } = default!;
    [JsonIgnore]
    public string UserId { get; set; } = default!;

    public DateTimeOffset Created { get; set; }
    public UserContributionStatus Status { get; set; } = UserContributionStatus.Pending;

    public ICollection<UserContributionDisc> Discs { get; set; } = new HashSet<UserContributionDisc>();
    public ICollection<UserContributionDiscHashItem> HashItems { get; set; } = new HashSet<UserContributionDiscHashItem>();

    public string MediaType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalProvider { get; set; } = string.Empty;
    public DateTimeOffset ReleaseDate { get; set; }
    public string Asin { get; set; } = string.Empty;
    public string Upc { get; set; } = string.Empty;
    public string FrontImageUrl { get; set; } = string.Empty;
    public string? BackImageUrl { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string? ReleaseSlug { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string RegionCode { get; set; } = string.Empty;

    // These are mostly used for display and lookup but are redundant data
    public string? Title { get; set; } = string.Empty;
    public string? Year { get; set; } = string.Empty;
    public string TitleSlug { get; set; } = string.Empty;
}
