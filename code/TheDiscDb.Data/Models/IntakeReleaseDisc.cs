namespace TheDiscDb.Web.Data;

using System;

public class IntakeReleaseDisc
{
    public int Id { get; set; }
    public int IntakeReleaseId { get; set; }
    public IntakeRelease Release { get; set; } = null!;
    public int IntakeDiscId { get; set; }
    public IntakeDisc Disc { get; set; } = null!;

    public string? SourceDiscId { get; set; }
    public string? GlobalDiscId { get; set; }
    public int? Index { get; set; }
    public string? Slug { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
