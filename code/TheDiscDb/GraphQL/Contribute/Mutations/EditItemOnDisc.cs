using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(DiscNotFoundException))]
    [Error(typeof(DiscItemNotFoundException))]
    public async Task<UserContributionDiscItem> EditItemOnDisc(
        [Service] SqlServerDataContext database,
        string contributionId,
        string discId,
        string itemId,
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
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var decodedDiscId = this.idEncoder.Decode(discId);
        var decodedItemId = this.idEncoder.Decode(itemId);

        var contribution = await database.UserContributions
                .Include(c => c.Discs.Where(d => d.Id == decodedDiscId))
                    .ThenInclude(d => d.Items.Where(i => i.Id == decodedItemId))
                .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        if (contribution == null)
        {
            throw new ContributionNotFoundException(contributionId);
        }

        var disc = contribution.Discs.FirstOrDefault();
        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        var item = disc.Items.FirstOrDefault();
        if (item == null)
        {
            throw new DiscItemNotFoundException(discId);
        }

        item.ChapterCount = chapterCount;
        item.Description = description ?? "";
        item.Duration = duration;
        item.Size = size;
        item.Name = name;
        item.SegmentCount = segmentCount;
        item.SegmentMap = segmentMap;
        item.Source = source;
        item.Type = type;
        item.Season = season ?? "";
        item.Episode = episode ?? "";

        await database.SaveChangesAsync(cancellationToken);

        return item;
    }
}
