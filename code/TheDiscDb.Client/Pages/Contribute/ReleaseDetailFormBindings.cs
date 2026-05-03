using System.ComponentModel.DataAnnotations;
using TheDiscDb.Client.Contributions;
using TheDiscDb.Validation;

namespace TheDiscDb.Client.Pages.Contribute;

/// <summary>
/// Validation wrapper that <see cref="ReleaseDetailInput"/>'s EditForm binds to.
/// All the properties are thin pass-throughs to the underlying StrawberryShake-
/// generated <see cref="ContributionMutationRequestInput"/>, so the existing
/// page logic that reads/writes <c>request.Asin</c>, <c>request.Upc</c>, etc.
/// continues to work unchanged. The DataAnnotations attributes on this wrapper
/// give us the client-side validation that the SS-generated input cannot carry
/// across the GraphQL boundary.
/// </summary>
public class ReleaseDetailFormBindings
{
    private readonly ContributionMutationRequestInput target;

    public ReleaseDetailFormBindings(ContributionMutationRequestInput target)
    {
        this.target = target;
    }

    [Required(ErrorMessage = "ASIN is required")]
    [Asin]
    public string? Asin
    {
        get => string.IsNullOrEmpty(target.Asin) ? null : target.Asin;
        set => target.Asin = value ?? string.Empty;
    }

    [Required(ErrorMessage = "UPC/EAN is required")]
    [Upc]
    public string? Upc
    {
        get => string.IsNullOrEmpty(target.Upc) ? null : target.Upc;
        set => target.Upc = value ?? string.Empty;
    }

    [Required(ErrorMessage = "Front Image is required")]
    public string? FrontImageUrl
    {
        get => target.FrontImageUrl;
        set => target.FrontImageUrl = value;
    }

    public string? BackImageUrl
    {
        get => target.BackImageUrl;
        set => target.BackImageUrl = value;
    }

    [Required(ErrorMessage = "Release Title is required")]
    public string? ReleaseTitle
    {
        get => string.IsNullOrEmpty(target.ReleaseTitle) ? null : target.ReleaseTitle;
        set => target.ReleaseTitle = value ?? string.Empty;
    }

    [Required(ErrorMessage = "Release Slug is required")]
    public string? ReleaseSlug
    {
        get => string.IsNullOrEmpty(target.ReleaseSlug) ? null : target.ReleaseSlug;
        set => target.ReleaseSlug = value ?? string.Empty;
    }

    [Required(ErrorMessage = "Locale is required")]
    public string? Locale
    {
        get => string.IsNullOrEmpty(target.Locale) ? null : target.Locale;
        set => target.Locale = value ?? string.Empty;
    }

    [Required(ErrorMessage = "Region Code is required")]
    public string? RegionCode
    {
        get => string.IsNullOrEmpty(target.RegionCode) ? null : target.RegionCode;
        set => target.RegionCode = value ?? string.Empty;
    }
}
