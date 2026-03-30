using FluentResults;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Boxset;

public interface IBoxsetValidation
{
    Task<Result> Validate(UserContributionBoxset boxset, CancellationToken cancellationToken);
    string DisplayName { get; }
}
