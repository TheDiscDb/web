namespace TheDiscDb.Web.Data;

using System;

public class IntakePromotion
{
    public int Id { get; set; }
    public IntakePromotionTarget Target { get; set; }
    public IntakePromotionStatus Status { get; set; } = IntakePromotionStatus.Pending;
    public IntakeSource Source { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? RequestedByUserId { get; set; }
    public string? FailureReason { get; set; }

    public int? IntakeReleaseId { get; set; }
    public IntakeRelease? Release { get; set; }
    public int? IntakeDiscId { get; set; }
    public IntakeDisc? Disc { get; set; }
    public int? UserContributionId { get; set; }
    public UserContribution? UserContribution { get; set; }

    public string? MediaItemSlug { get; set; }
    public string? BoxsetSlug { get; set; }
    public string? ReleaseSlug { get; set; }
    public string? DiscFormat { get; set; }
    public string? DiscContentHash { get; set; }
    public string? DiscGlobalId { get; set; }
    public string? EvidenceSetId { get; set; }
}
