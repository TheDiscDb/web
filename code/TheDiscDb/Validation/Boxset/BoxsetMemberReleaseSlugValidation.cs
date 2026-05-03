using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Boxset;

/// <summary>
/// Verifies every boxset member's release slug matches the boxset's own slug.
/// The current data layout uses <c>boxset.Slug</c> as the release directory name
/// when the import-time resolver locates each member on disk
/// (<c>DataImportItemFactory.FindBoxsetDisc</c>). A mismatched member would not
/// be found at import time, leaving the boxset effectively empty.
/// Add-time mutations also enforce this — this validation catches anything that
/// slipped through (e.g., the boxset slug being edited after members were added).
/// </summary>
public class BoxsetMemberReleaseSlugValidation : IBoxsetValidation
{
    public string DisplayName => "All Members Share Boxset Slug";

    public Task<Result> Validate(UserContributionBoxset boxset, CancellationToken cancellationToken)
    {
        if (boxset.Members == null || boxset.Members.Count == 0 || string.IsNullOrWhiteSpace(boxset.Slug))
        {
            return Task.FromResult(Result.Ok());
        }

        var mismatches = new List<string>();

        foreach (var member in boxset.Members)
        {
            string? memberReleaseSlug = null;
            string memberLabel = "Unknown";

            if (member.Disc?.UserContribution != null)
            {
                memberReleaseSlug = member.Disc.UserContribution.ReleaseSlug;
                memberLabel = member.Disc.Name ?? member.Disc.UserContribution.Title ?? "Unknown";
            }
            else if (!string.IsNullOrEmpty(member.ExistingDiscPath))
            {
                // existingDiscPath is "{type}/{tmdbId}/{releaseSlug}/{discKey}"
                try
                {
                    var (_, _, releaseSlug, _) = UserContributionDisc.ParseDiscPath(member.ExistingDiscPath);
                    memberReleaseSlug = releaseSlug;
                    memberLabel = member.ExistingDiscName ?? member.ExistingDiscPath;
                }
                catch
                {
                    // Malformed path — surface this as a validation failure rather than silently
                    // passing. There's no other review-time validator that catches path format.
                    mismatches.Add($"{member.ExistingDiscName ?? member.ExistingDiscPath} (invalid disc path: '{member.ExistingDiscPath}')");
                    continue;
                }
            }
            else
            {
                continue;
            }

            if (!string.Equals(memberReleaseSlug, boxset.Slug, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add($"{memberLabel} (release slug: '{memberReleaseSlug ?? string.Empty}')");
            }
        }

        if (mismatches.Count > 0)
        {
            return Task.FromResult(Result.Fail(
                $"The following members have a release slug different from the boxset's slug ('{boxset.Slug}'). Boxsets currently require every member to share the boxset's slug as its release slug: {string.Join("; ", mismatches)}"));
        }

        return Task.FromResult(Result.Ok());
    }
}
