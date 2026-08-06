namespace TheDiscDb.Data.Changes.ReleasePartial;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public sealed class ReleasePartialUpdate : ChangeBase<ReleasePartialDetails>
{
    public const string Key = "release.partial.update";

    public ReleasePartialUpdate(ReleasePartialDetails proposed)
        : base(proposed)
    {
    }

    public ReleasePartialUpdate(ReleasePartialDetails proposed, JsonSerializerOptions jsonOptions)
        : base(proposed, jsonOptions)
    {
    }

    public override string TypeKey => Key;

    public override string TargetEntityKey => this.Proposed.TargetEntityKey;

    protected override async Task<ReleasePartialDetails?> LoadCurrentSnapshotAsync(
        SqlServerDataContext context,
        CancellationToken cancellationToken)
    {
        var release = await ResolveReleaseAsync(context, this.Proposed, cancellationToken);
        return release is null
            ? null
            : SnapshotFrom(release, this.Proposed.MediaItemSlug, this.Proposed.BoxsetSlug);
    }

    protected override async Task ApplyCoreAsync(
        SqlServerDataContext context,
        IChangeApplyContext apply,
        ReleasePartialDetails? original,
        CancellationToken cancellationToken)
    {
        var release = await ResolveReleaseAsync(context, this.Proposed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Release '{this.Proposed.TargetEntityKey}' not found at apply time.");

        SetIfChanged(
            original,
            original?.Partial,
            this.Proposed.Partial,
            value => release.Partial = value is null ? null : value with { });
    }

    protected override Task<ChangeValidationResult?> ValidateAdditionalAsync(
        SqlServerDataContext context,
        ReleasePartialDetails? original,
        ReleasePartialDetails current,
        CancellationToken cancellationToken)
    {
        try
        {
            this.Proposed.Partial?.Validate(PartialStateTarget.Release);
            return Task.FromResult<ChangeValidationResult?>(null);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult<ChangeValidationResult?>(
                ChangeValidationResult.Conflict(ex.Message));
        }
    }

    protected override string? DescribeDrift(ReleasePartialDetails original, ReleasePartialDetails current)
    {
        if (original.MediaItemSlug != current.MediaItemSlug
            || original.BoxsetSlug != current.BoxsetSlug
            || original.ReleaseSlug != current.ReleaseSlug)
        {
            return $"Release identity changed: snapshot '{original.TargetEntityKey}' vs current '{current.TargetEntityKey}'.";
        }

        return original.Partial == current.Partial
            ? null
            : "Release partial state has changed since the suggestion was submitted.";
    }

    protected override string MissingTargetMessage() =>
        $"Release '{this.Proposed.TargetEntityKey}' no longer exists.";

    public static ReleasePartialDetails SnapshotFrom(
        Release release,
        string? mediaItemSlug,
        string? boxsetSlug) =>
        new(mediaItemSlug, boxsetSlug, release.Slug ?? string.Empty, release.Partial is null ? null : release.Partial with { });

    private static async Task<Release?> ResolveReleaseAsync(
        SqlServerDataContext context,
        ReleasePartialDetails details,
        CancellationToken cancellationToken)
    {
        var hasMedia = !string.IsNullOrWhiteSpace(details.MediaItemSlug);
        var hasBoxset = !string.IsNullOrWhiteSpace(details.BoxsetSlug);
        if (hasMedia == hasBoxset)
        {
            return null;
        }

        if (hasMedia)
        {
            var parentSlug = details.MediaItemSlug;
            return await context.Releases.FirstOrDefaultAsync(
                r => r.Slug == details.ReleaseSlug
                    && r.MediaItem != null
                    && r.MediaItem.Slug == parentSlug,
                cancellationToken);
        }

        var boxsetSlug = details.BoxsetSlug;
        return await context.Releases.FirstOrDefaultAsync(
            r => r.Slug == details.ReleaseSlug
                && r.Boxset != null
                && r.Boxset.Slug == boxsetSlug,
            cancellationToken);
    }
}
