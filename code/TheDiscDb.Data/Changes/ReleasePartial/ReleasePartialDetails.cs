namespace TheDiscDb.Data.Changes.ReleasePartial;

public sealed record ReleasePartialDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    PartialState? Partial)
{
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}";
}
