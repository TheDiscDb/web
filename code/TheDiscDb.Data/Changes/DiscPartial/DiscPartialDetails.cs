namespace TheDiscDb.Data.Changes.DiscPartial;

using TheDiscDb.Data.Changes.DiscFields;

public sealed record DiscPartialDetails(
    string? MediaItemSlug,
    string? BoxsetSlug,
    string ReleaseSlug,
    string? DiscSlug,
    int DiscIndex,
    PartialState? Partial)
{
    public string TargetEntityKey =>
        $"{(this.MediaItemSlug ?? this.BoxsetSlug) ?? string.Empty}/{this.ReleaseSlug}/{DiscFieldsDetails.DiscToken(this.DiscSlug, this.DiscIndex)}";
}
