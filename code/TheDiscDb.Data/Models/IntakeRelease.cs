namespace TheDiscDb.Web.Data;

using System;
using System.Collections.Generic;

public class IntakeRelease
{
    private string externalProvider = string.Empty;
    private string externalId = string.Empty;
    private string upc = string.Empty;

    public int Id { get; set; }
    public IntakeSource Source { get; set; }
    public string SourceReleaseId { get; set; } = string.Empty;
    public IntakeStatus Status { get; set; } = IntakeStatus.Pending;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public string? MediaItemSlug { get; set; }
    public string? BoxsetSlug { get; set; }
    public string? ReleaseSlug { get; set; }
    public string ExternalProvider
    {
        get => this.externalProvider;
        set => this.externalProvider = IntakeMatchNormalizer.ExternalProvider(value);
    }
    public string ExternalId
    {
        get => this.externalId;
        set => this.externalId = IntakeMatchNormalizer.ExternalId(value);
    }
    public string Upc
    {
        get => this.upc;
        set => this.upc = IntakeMatchNormalizer.Upc(value);
    }
    public string? Asin { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public string? ReleaseTitle { get; set; }
    public string? Locale { get; set; }
    public string? RegionCode { get; set; }
    public string? FrontImageLocation { get; set; }

    public ICollection<IntakeReleaseDisc> Discs { get; set; } = new HashSet<IntakeReleaseDisc>();
    public ICollection<IntakePromotion> Promotions { get; set; } = new HashSet<IntakePromotion>();
}
