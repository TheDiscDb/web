using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.GraphQL.Contribute.Models;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<DiscHash> HashDisc(string contributionId, List<FileHashInfo> files, SqlServerDataContext database, CancellationToken cancellationToken = default)
    {
        int id = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.HashItems)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        await EnsureOwnership(contribution, contributionId, cancellationToken: cancellationToken);

        var hash = files.OrderBy(f => f.Name).CalculateHash();
        var existingItems = contribution!.HashItems?.Where(i => i.DiscHash == hash).ToList();
        foreach (var existing in existingItems ?? Enumerable.Empty<UserContributionDiscHashItem>())
        {
            contribution.HashItems!.Remove(existing);
            database.UserContributionDiscHashItems.Remove(existing);
        }

        foreach (var item in files)
        {
            contribution.HashItems!.Add(new UserContributionDiscHashItem
            {
                DiscHash = hash,
                CreationTime = item.CreationTime,
                Index = item.Index,
                Name = item.Name ?? "",
                Size = item.Size
            });
        }

        await database.SaveChangesAsync(cancellationToken);

        var response = new DiscHash(hash);
        return response;
    }
}
