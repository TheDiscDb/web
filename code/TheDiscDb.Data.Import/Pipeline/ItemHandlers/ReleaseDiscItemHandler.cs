namespace TheDiscDb.Data.Import.Pipeline;

using System;
using TheDiscDb.InputModels;

public class ReleaseDiscItemHandler : ItemHandler<ReleaseDisc>
{
    private readonly IItemHandler<Disc> discItemHandler;

    public ReleaseDiscItemHandler(IItemHandler<Disc> discItemHandler)
    {
        this.discItemHandler = discItemHandler ?? throw new ArgumentNullException(nameof(discItemHandler));
    }

    public override bool IsMatch(ReleaseDisc d1, ReleaseDisc d2)
    {
        if (d1 == null || d2 == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(d1.Slug) && !string.IsNullOrEmpty(d2.Slug) && d1.Slug.Equals(d2.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return d1.Index == d2.Index;
    }

    public override void TryUpdate(ReleaseDisc fromDatabase, ReleaseDisc newValue)
    {
        fromDatabase.Index = newValue.Index;
        fromDatabase.Name = newValue.Name;
        fromDatabase.Slug = newValue.Slug;

        if (newValue.Disc == null)
        {
            return;
        }

        if (fromDatabase.Disc == null)
        {
            fromDatabase.Disc = newValue.Disc;
            return;
        }

        // In the canonical Disc model, a ReleaseDisc may need to point to a different
        // canonical Disc on update. If we mutate the existing Disc payload in-place and
        // then swap links later, the orphaned modified Disc can violate the unique
        // (Format, ContentHash) index. Re-point first when payloads don't represent
        // the same disc.
        if (!this.discItemHandler.IsMatch(fromDatabase.Disc, newValue.Disc))
        {
            fromDatabase.Disc = newValue.Disc;
            return;
        }

        this.discItemHandler.TryUpdate(fromDatabase.Disc, newValue.Disc);
    }
}
