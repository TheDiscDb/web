using System.ComponentModel.DataAnnotations;

namespace TheDiscDb.GraphQL.Contribute.Models;

public class BoxsetMutationRequest
{
    [Required]
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    [Required]
    public string Slug { get; set; } = string.Empty;
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; }
    public string? Asin { get; set; }
    public string? Upc { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public string? Locale { get; set; }
    public string? RegionCode { get; set; }
}
