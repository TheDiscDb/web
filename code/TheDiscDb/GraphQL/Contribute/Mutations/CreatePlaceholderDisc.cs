using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    /// <summary>
    /// Adds a placeholder disc to a contribution's release: a known-missing disc with a
    /// name/slug/format but no content hash, logs, summary, or identified items. On generation
    /// this produces a <c>discNN.placeholder.json</c> file, and marks the release partial.
    /// </summary>
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserContributionDisc> CreatePlaceholderDisc(
        string contributionId,
        string format,
        string name,
        string slug,
        [Service] SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        CancellationToken cancellationToken = default)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(userManager, contribution, contributionId, cancellationToken: cancellationToken);

        // If a placeholder with the same slug already exists, update it in place rather than
        // creating a duplicate slot.
        var existing = contribution!.Discs.FirstOrDefault(d => d.IsPlaceholder && d.Slug == slug);
        if (existing != null)
        {
            existing.Format = format;
            existing.Name = name;
            await database.SaveChangesAsync(cancellationToken);
            return existing;
        }

        // Normalize any existing null indices before computing max.
        var existingDiscs = contribution.Discs
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

        int maxIndex = existingDiscs.Any() ? existingDiscs.Max(d => d.Index!.Value) : 0;

        var disc = new UserContributionDisc
        {
            ContentHash = string.Empty,
            Format = format,
            Name = name,
            Slug = slug,
            ExistingDiscPath = string.Empty,
            IsPlaceholder = true,
            Index = maxIndex + 1
        };

        contribution.Discs.Add(disc);
        await database.SaveChangesAsync(cancellationToken);
        return disc;
    }
}
