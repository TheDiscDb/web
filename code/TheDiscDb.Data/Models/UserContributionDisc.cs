namespace TheDiscDb.Web.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using HotChocolate;

public class UserContributionDisc : IHasId
{
    [JsonIgnore]
    public int Id { get; set; }
    [NotMapped]
    [GraphQLIgnore]
    public string EncodedId { get; set; } = default!;
    [JsonIgnore]
    public UserContribution UserContribution { get; set; } = default!;
    public string ContentHash { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool LogsUploaded { get; set; } = false;
    public string? LogUploadError { get; set; }
    public string? ExistingDiscPath { get; set; } = default!;

    public ICollection<UserContributionDiscItem> Items { get; set; } = new HashSet<UserContributionDiscItem>();

    public static string GenerateDiscPath(string mediaType, string externalId, string releaseSlug, string discSlug) => $"{mediaType}/{externalId}/{releaseSlug}/{discSlug}";
    public static (string MediaType, string ExternalId, string ReleaseSlug, string DiscSlug) ParseDiscPath(string discPath)
    {
        if (string.IsNullOrEmpty(discPath))
        {
            throw new ArgumentException("Disc path cannot be null or empty", nameof(discPath));
        }

        var parts = discPath.Split('/');
        if (parts.Length != 4)
        {
            throw new ArgumentException("Invalid disc path format", nameof(discPath));
        }

        return (parts[0], parts[1], parts[2], parts[3]);
    }
}
