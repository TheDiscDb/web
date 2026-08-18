namespace TheDiscDb.Web.Data;

using System;

public class IntakeDiscEvidence
{
    public long Id { get; set; }
    public int IntakeDiscId { get; set; }
    public IntakeDisc Disc { get; set; } = null!;
    public IntakeSource Source { get; set; }
    public string SourceEvidenceId { get; set; } = string.Empty;
    public string EvidenceSetId { get; set; } = string.Empty;
    public string? GlobalDiscId { get; set; }
    public IntakeDiscEvidenceType Type { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? Sha256 { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
