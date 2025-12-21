using FluentResults;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Validation.Contribution;

public class UniqueReleaseSlugValidation : IContributionValidation
{
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;

    public UniqueReleaseSlugValidation(IDbContextFactory<SqlServerDataContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public string DisplayName => "Unique Release Slug";

    public async Task<Result> Validate(UserContribution contribution, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        {
            var externalIds = await dbContext.ExternalIds
                .Include(i => i.MediaItem)
                .Where(m => m.Tmdb == contribution.ExternalId)
                .ToListAsync(cancellationToken);

            if (externalIds == null || externalIds.Count == 0)
            {
                return Result.Ok();
            }

            foreach (var externalId in externalIds)
            {
                var releases = await dbContext.Releases
                    .Where(r => r.MediaItem!.Id == externalId.MediaItem!.Id)
                    .ToListAsync(cancellationToken);

                if (releases != null && releases.Count > 0)
                {
                    if (releases.Any(r => !String.IsNullOrEmpty(r.Slug) && r.Slug.Equals(contribution.ReleaseSlug, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Result.Fail($"The release slug {contribution.ReleaseSlug} is not unique. Conflict with {externalId.MediaItem!.Slug}");
                    }
                }
            }

            return Result.Ok();
        }
    }
}