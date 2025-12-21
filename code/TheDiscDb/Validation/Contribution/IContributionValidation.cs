using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Contribution;

public interface IContributionValidation
{
    Task<Result> Validate(UserContribution contribution, CancellationToken cancellationToken);
    string DisplayName { get; }
}
