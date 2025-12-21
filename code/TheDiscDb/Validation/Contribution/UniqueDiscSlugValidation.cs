using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Contribution;

public class UniqueDiscSlugValidation : IContributionValidation
{
    public string DisplayName => "Unique Disc Slugs";

    public Task<Result> Validate(UserContribution contribution, CancellationToken cancellationToken)
    {
        if (contribution.Discs == null || contribution.Discs.Count == 0)
        {
            return Task.FromResult(Result.Fail("The release must have at least one disc."));
        }

        var duplicates = contribution.Discs.GroupBy(x => x.Slug)
            .Where(g => g.Count() > 1)
            .Select(y => y.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            string duplicateList = string.Join(", ", duplicates);
            return Task.FromResult(Result.Fail("Duplicate disc slugs found: " + duplicateList));
        }

        return Task.FromResult(Result.Ok());
    }
}
