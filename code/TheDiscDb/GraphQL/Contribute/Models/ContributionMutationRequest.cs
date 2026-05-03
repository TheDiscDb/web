using System.ComponentModel.DataAnnotations;
using TheDiscDb.Validation;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Models;

public class ContributionMutationRequest
{
    [Required]
    public string MediaType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalProvider { get; set; } = string.Empty;
    [Required]
    public DateTimeOffset ReleaseDate { get; set; }
    [Required]
    [Asin]
    public string Asin { get; set; } = string.Empty;
    [Required]
    [Upc]
    public string Upc { get; set; } = string.Empty;

    [Required(ErrorMessage = "Front Image is required")]
    public string? FrontImageUrl { get; set; }
    public string? BackImageUrl { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    [Required]
    public string ReleaseSlug { get; set; } = string.Empty;
    [Required]
    public string RegionCode { get; set; } = string.Empty;
    [Required]
    public string Locale { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public Guid StorageId { get; set; } = Guid.Empty;
    public UserContributionStatus Status { get; set; } = UserContributionStatus.Pending;

    /// <summary>
    /// Optional encoded boxset id. When provided, the new contribution will be linked to the
    /// boxset and any discs subsequently added will be auto-included as boxset members.
    /// </summary>
    public string? BoxsetId { get; set; }

    public ContributionMutationRequest() { }

    public ContributionMutationRequest(UserContribution contribution)
    {
        this.MediaType = contribution.MediaType;
        this.ExternalId = contribution.ExternalId ?? string.Empty;
        this.ExternalProvider = contribution.ExternalProvider ?? string.Empty;
        this.ReleaseDate = contribution.ReleaseDate;
        this.Asin = contribution.Asin;
        this.Upc = contribution.Upc;
        this.FrontImageUrl = contribution.FrontImageUrl;
        this.BackImageUrl = contribution.BackImageUrl ?? string.Empty;
        this.ReleaseTitle = contribution.ReleaseTitle ?? string.Empty;
        this.ReleaseSlug = contribution.ReleaseSlug ?? string.Empty;
        this.RegionCode = contribution.RegionCode;
        this.Locale = contribution.Locale;
        this.Title = contribution.Title ?? string.Empty;
        this.Year = contribution.Year ?? string.Empty;
        this.Status = contribution.Status;
    }
}
