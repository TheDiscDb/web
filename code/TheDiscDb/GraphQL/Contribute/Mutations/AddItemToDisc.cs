using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(DiscNotFoundException))]
    public async Task<UserContributionDiscItem> AddItemToDisc(
        [Service] SqlServerDataContext database,
        string contributionId, 
        string discId,
        string name,
        string source,
        string duration,
        string size,
        int chapterCount,
        int segmentCount,
        string segmentMap,
        string type,
        string? description = null,
        string? season = null,
        string? episode = null,
        CancellationToken cancellationToken = default)
    {
        var item = new UserContributionDiscItem
        {
            ChapterCount = chapterCount,
            Description = description ?? "",
            Duration = duration,
            Size = size,
            Name = name,
            SegmentCount = segmentCount,
            SegmentMap = segmentMap,
            Source = source,
            Type = type,
            Season = season ?? "",
            Episode = episode ?? ""
        };

        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
                .ThenInclude(d => d.Items)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        if (contribution == null)
        {
            throw new ContributionNotFoundException(contributionId);
        }

        int realDiscId = this.idEncoder.Decode(discId);
        var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        disc.Items.Add(item);
        await database.SaveChangesAsync(cancellationToken);
        return item;
    }
}
