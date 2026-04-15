namespace TheDiscDb.Web.Data;

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using HotChocolate;

public class UserContributionChapter : IHasId
{
    [JsonIgnore]
    public int Id { get; set; }
    [NotMapped]
    [GraphQLIgnore]
    public string EncodedId { get; set; } = default!;
    public int Index { get; set; }
    public string Title { get; set; } = default!;
    [JsonIgnore]
    public UserContributionDiscItem Item { get; set; } = default!;
}
