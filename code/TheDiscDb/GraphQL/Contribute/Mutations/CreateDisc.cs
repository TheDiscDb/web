using FluentResults;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    public async Task<UserContributionDisc> CreateDisc(string contributionId, string contentHash, string format, string name, string slug, [Service] SqlServerDataContext database, string? existingDiscPath = null, CancellationToken cancellationToken = default)
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

        if (contribution == null)
        {
            throw new ContributionNotFoundException(contributionId);
        }

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
            contribution!.Discs.Add(disc);
        }

        await database.SaveChangesAsync(cancellationToken);
        return disc;
    }
}
