using FluentResults;
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
    public async Task<UserContributionDisc> CreateDisc(string contributionId, string contentHash, string format, string name, string slug, [Service] SqlServerDataContext database, UserManager<TheDiscDbUser> userManager, string? existingDiscPath = null, CancellationToken cancellationToken = default)
    {
        var disc = new UserContributionDisc
        {
            ContentHash = contentHash,
            Format = format,
            Name = name,
            Slug = slug,
            ExistingDiscPath = existingDiscPath ?? ""
        };

        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(userManager, contribution, contributionId, cancellationToken: cancellationToken);

        var existingDisc = contribution?.Discs.FirstOrDefault(d => d.ContentHash == disc.ContentHash);
        if (existingDisc != null)
        {
            existingDisc.Format = format;
            existingDisc.Name = name;
            existingDisc.Slug = slug;
            existingDisc.ExistingDiscPath = existingDiscPath ?? "";
            await database.SaveChangesAsync(cancellationToken);
            return existingDisc;
        }
        else
        {
            // Normalize any existing null indices before computing max
            var existingDiscs = contribution!.Discs
                .OrderBy(d => d.Index ?? int.MaxValue)
                .ThenBy(d => d.Id)
                .ToList();

            for (int i = 0; i < existingDiscs.Count; i++)
            {
                if (existingDiscs[i].Index == null)
                {
                    existingDiscs[i].Index = i + 1;
                }
            }

            int maxIndex = existingDiscs.Any()
                ? existingDiscs.Max(d => d.Index!.Value)
                : 0;
            disc.Index = maxIndex + 1;
            contribution.Discs.Add(disc);
        }

        await database.SaveChangesAsync(cancellationToken);
        return disc;
    }
}
