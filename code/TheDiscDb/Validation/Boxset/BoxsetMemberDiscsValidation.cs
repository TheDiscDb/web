using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Boxset;

public class BoxsetMemberDiscsValidation : IBoxsetValidation
{
    public string DisplayName => "All Discs Have Contributions";

    public Task<Result> Validate(UserContributionBoxset boxset, CancellationToken cancellationToken)
    {
        if (boxset.Members == null || boxset.Members.Count == 0)
        {
            return Task.FromResult(Result.Ok());
        }

        // Two kinds of boxset members:
        //   1. Contribution-backed: m.Disc points at a UserContributionDisc whose parent is a
        //      UserContribution. Those must have a non-null UserContribution before the boxset
        //      can be submitted.
        //   2. Existing-database-backed: m.ExistingDiscPath references a Disc that already lives
        //      in the published database. There is no contribution for those, by design.
        // Only the first kind needs a parent contribution.
        var discsWithoutContribution = boxset.Members
            .Where(m => string.IsNullOrEmpty(m.ExistingDiscPath))
            .Where(m => m.Disc?.UserContribution == null)
            .Select(m => m.Disc?.Name ?? "Unknown")
            .ToList();

        if (discsWithoutContribution.Count > 0)
        {
            return Task.FromResult(Result.Fail($"The following discs have no parent contribution: {string.Join(", ", discsWithoutContribution)}"));
        }

        return Task.FromResult(Result.Ok());
    }
}
