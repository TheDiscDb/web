using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(DiscNotFoundException))]
    [Error(typeof(DiscItemNotFoundException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<UserContributionAudioTrack> AddAudioTrackToItem(string contributionId, string discId, string itemId, int trackIndex, string trackName, SqlServerDataContext database, CancellationToken cancellationToken)
    {
        var audioTrack = new UserContributionAudioTrack
        {
            Index = trackIndex,
            Title = trackName
        };

        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .Include(c => c.Discs)
                .ThenInclude(d => d.Items)
                    .ThenInclude(i => i.AudioTracks)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(contribution, contributionId, discId, itemId, cancellationToken);

        int realDiscId = this.idEncoder.Decode(discId);
        var disc = contribution!.Discs.FirstOrDefault(d => d.Id == realDiscId);

        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        int realItemId = this.idEncoder.Decode(itemId);
        var item = disc.Items.FirstOrDefault(i => i.Id == realItemId);

        if (item == null)
        {
            throw new DiscItemNotFoundException(itemId);
        }

        var existingTrack = item.AudioTracks.FirstOrDefault(c => c.Index == audioTrack.Index);
        if (existingTrack != null)
        {
            existingTrack.Title = audioTrack.Title;
            await database.SaveChangesAsync(cancellationToken);
            return existingTrack;
        }
        else
        {
            item.AudioTracks.Add(audioTrack);
            await database.SaveChangesAsync(cancellationToken);
            return audioTrack;
        }
    }
}
