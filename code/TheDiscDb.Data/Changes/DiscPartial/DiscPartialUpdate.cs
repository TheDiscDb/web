namespace TheDiscDb.Data.Changes.DiscPartial;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public sealed class DiscPartialUpdate : ChangeBase<DiscPartialDetails>
{
    public const string Key = "disc.partial.update";

    public DiscPartialUpdate(DiscPartialDetails proposed)
        : base(proposed)
    {
    }

    public DiscPartialUpdate(DiscPartialDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<DiscPartialDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var releaseDisc = await ResolveDiscAsync(context, this.Proposed, cancellationToken);
        return releaseDisc?.Disc is null
            ? null
            : SnapshotFrom(releaseDisc, this.Proposed.MediaItemSlug, this.Proposed.BoxsetSlug, this.Proposed.ReleaseSlug);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        DiscPartialDetails? original,
        CancellationToken cancellationToken)
    {
        var releaseDisc = await ResolveDiscAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Disc '{this.Proposed.TargetEntityKey}' not found at apply time.");
        var disc = releaseDisc.Disc
            ?? throw new InvalidOperationException(
                $"Canonical disc for '{this.Proposed.TargetEntityKey}' not found at apply time.");

        SetIfChanged(
            original,
            original?.Partial,
            this.Proposed.Partial,
            value => disc.Partial = value is null ? null : value with { });
    }

    protected override Task<ChangeValidationResult?> ValidateAdditionalAsync(
        SqlServerDataContext context,
        DiscPartialDetails? original,
        DiscPartialDetails current,
        CancellationToken cancellationToken)
    {
        try
        {
            this.Proposed.Partial?.Validate(PartialStateTarget.Disc);
            return Task.FromResult<ChangeValidationResult?>(null);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult<ChangeValidationResult?>(
                ChangeValidationResult.Conflict(ex.Message));
        }
    }

    protected override string? DescribeDrift(DiscPartialDetails original, DiscPartialDetails current)
    {
        if (original.MediaItemSlug != current.MediaItemSlug
            || original.BoxsetSlug != current.BoxsetSlug
            || original.ReleaseSlug != current.ReleaseSlug
            || original.DiscSlug != current.DiscSlug
            || original.DiscIndex != current.DiscIndex)
        {
            return $"Disc identity changed: snapshot '{original.TargetEntityKey}' vs current '{current.TargetEntityKey}'.";
        }

        return original.Partial == current.Partial
            ? null
            : "Disc partial state has changed since the suggestion was submitted.";
    }

    protected override string MissingTargetMessage() =>
        $"Disc '{this.Proposed.TargetEntityKey}' no longer exists.";

    public static DiscPartialDetails SnapshotFrom(
        ReleaseDisc releaseDisc,
        string? mediaItemSlug,
        string? boxsetSlug,
        string releaseSlug) =>
        new(
            mediaItemSlug,
            boxsetSlug,
            releaseSlug,
            releaseDisc.Slug,
            releaseDisc.Index,
            releaseDisc.Disc?.Partial is null ? null : releaseDisc.Disc.Partial with { });

    public static Task<ReleaseDisc?> ResolveDiscAsync(
        SqlServerDataContext context,
        DiscPartialDetails details,
        CancellationToken cancellationToken) =>
        DiscFieldsUpdate.ResolveDiscAsync(
            context,
            new DiscFieldsDetails(
                details.MediaItemSlug,
                details.BoxsetSlug,
                details.ReleaseSlug,
                details.DiscSlug,
                details.DiscIndex,
                Name: null,
                Format: null),
            cancellationToken);
}
