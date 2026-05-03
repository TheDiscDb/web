using System.ComponentModel.DataAnnotations;
using TheDiscDb.Validation;

namespace TheDiscDb.Client.Pages.Contribute;

// Wrapper that the boxset Create + Edit EditForms bind to so client-side
// DataAnnotations validation works. The StrawberryShake-generated
// BoxsetMutationRequestInput cannot carry attributes across the GraphQL
// boundary, so we mirror its shape here and map on submit.
public class BoxsetEditableRequest
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string? SortTitle { get; set; }

    [Required]
    public string Slug { get; set; } = string.Empty;

    [Required(ErrorMessage = "ASIN is required")]
    [Asin]
    public string? Asin { get; set; }

    [Required(ErrorMessage = "UPC/EAN is required")]
    [Upc]
    public string? Upc { get; set; }

    [Required(ErrorMessage = "Release Date is required")]
    public DateTimeOffset? ReleaseDate { get; set; }

    public string? Locale { get; set; }
    public string? RegionCode { get; set; }
}
