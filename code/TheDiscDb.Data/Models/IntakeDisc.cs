namespace TheDiscDb.Web.Data;

using System;
using System.Collections.Generic;

public class IntakeDisc
{
    private string format = string.Empty;
    private string contentHash = string.Empty;

    public int Id { get; set; }
    public string Format
    {
        get => this.format;
        set => this.format = IntakeMatchNormalizer.Format(value);
    }
    public string ContentHash
    {
        get => this.contentHash;
        set => this.contentHash = IntakeMatchNormalizer.ContentHash(value);
    }
    public IntakeStatus Status { get; set; } = IntakeStatus.Pending;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<IntakeReleaseDisc> Releases { get; set; } = new HashSet<IntakeReleaseDisc>();
    public ICollection<IntakeDiscEvidence> Evidence { get; set; } = new HashSet<IntakeDiscEvidence>();
    public ICollection<IntakePromotion> Promotions { get; set; } = new HashSet<IntakePromotion>();
}
