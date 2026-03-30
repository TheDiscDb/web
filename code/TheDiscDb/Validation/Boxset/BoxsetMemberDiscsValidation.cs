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

        var discsWithoutContribution = boxset.Members
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
