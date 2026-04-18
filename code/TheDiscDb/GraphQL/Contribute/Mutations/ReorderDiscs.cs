using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<IReadOnlyList<UserContributionDisc>> ReorderDiscs(
        string contributionId,
        string[] discIds,
        [Service] SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(userManager, contribution, contributionId, cancellationToken: cancellationToken);

        var discLookup = contribution!.Discs.ToDictionary(d => d.Id);

        if (discIds.Length != discLookup.Count || discIds.Distinct().Count() != discIds.Length)
        {
            throw new InvalidOperationException("discIds must contain exactly one entry for each disc in the contribution.");
        }

        for (int i = 0; i < discIds.Length; i++)
        {
            int decodedDiscId = this.idEncoder.Decode(discIds[i]);
            if (decodedDiscId == 0)
            {
                throw new InvalidIdException(discIds[i], "Disc");
            }

            if (!discLookup.TryGetValue(decodedDiscId, out var disc))
            {
                throw new DiscNotFoundException(discIds[i]);
            }

            disc.Index = i + 1;
        }

        await database.SaveChangesAsync(cancellationToken);

        return contribution.Discs
            .OrderBy(d => d.Index ?? int.MaxValue)
            .ThenBy(d => d.Id)
            .ToList();
    }
}
