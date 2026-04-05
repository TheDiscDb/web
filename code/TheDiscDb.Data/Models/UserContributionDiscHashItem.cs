namespace TheDiscDb.Web.Data;

using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using HotChocolate;

public class UserContributionDiscHashItem : IHasId
{
    [JsonIgnore]
    public int Id { get; set; }
    [NotMapped]
    [GraphQLIgnore]
    public string EncodedId { get; set; } = default!;
    [JsonIgnore]
    public UserContribution UserContribution { get; set; } = default!;
    public string DiscHash { get; set; } = default!;
    public int Index { get; set; }
    public string Name { get; set; } = default!;
    public DateTime CreationTime { get; set; }
    public long Size { get; set; }
}
