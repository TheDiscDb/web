using Sqids;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services.Server;

public class IdEncoder
{
    private readonly SqidsEncoder<int> idEncoder;

    public IdEncoder(SqidsEncoder<int> idEncoder)
    {
        this.idEncoder = idEncoder ?? throw new ArgumentNullException(nameof(idEncoder));
    }

    public int Decode(string id)
    {
        return this.idEncoder.Decode(id).Single();
    }

    public string Encode(int id)
    {
        return this.idEncoder.Encode(id);
    }

    public void EncodeInPlace(IEnumerable<UserContribution>? contributions)
    {
        if (contributions == null)
        {
            return;
        }

        foreach (var contribution in contributions)
        {
            EncodeInPlace(contribution);
        }
    }

    public void EncodeInPlace(UserContribution? contribution)
    {
        if (contribution == null)
        {
            return;
        }

        contribution.EncodedId = this.idEncoder.Encode(contribution.Id);
        foreach (var disc in contribution.Discs)
        {
            EncodeInPlace(disc);
        }

        foreach (var item in contribution.HashItems)
        {
            EncodeInPlace(item);
        }
    }

    public void EncodeInPlace(UserContributionDisc? disc)
    {
        if (disc == null)
        {
            return;
        }

        disc.EncodedId = this.idEncoder.Encode(disc.Id);

        foreach (var item in disc.Items)
        {
            EncodeInPlace(item);
        }

        foreach (var item in disc.Items)
        {
            EncodeInPlace(item);
        }
    }

    public void EncodeInPlace(UserContributionDiscItem? item)
    {
        if (item == null)
        {
            return;
        }

        item.EncodedId = this.idEncoder.Encode(item.Id);

        foreach (var chapter in item.Chapters)
        {
            EncodeInPlace(chapter);
        }

        foreach (var audioTrack in item.AudioTracks)
        {
            EncodeInPlace(audioTrack);
        }
    }

    public void EncodeInPlace(UserContributionChapter? chapter)
    {
        chapter?.EncodedId = this.idEncoder.Encode(chapter.Id);
    }

    public void EncodeInPlace(UserContributionAudioTrack? track)
    {
        track?.EncodedId = this.idEncoder.Encode(track.Id);
    }

    public void EncodeInPlace(UserContributionDiscHashItem? item)
    {
        item?.EncodedId = this.idEncoder.Encode(item.Id);
    }
}
