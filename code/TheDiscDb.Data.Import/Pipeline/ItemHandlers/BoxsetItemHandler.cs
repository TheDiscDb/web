namespace TheDiscDb.Data.Import.Pipeline;

using System;
using TheDiscDb.InputModels;

public class BoxsetItemHandler : ItemHandler<Boxset>
{
    private readonly IItemHandler<Release> releaseItemHandler;

    public BoxsetItemHandler(IItemHandler<Release> releaseItemHandler)
    {
        this.releaseItemHandler = releaseItemHandler ?? throw new ArgumentNullException(nameof(releaseItemHandler));
    }

    public override bool IsMatch(Boxset item1, Boxset item2)
    {
        if (item1 == null || item2 == null)
        {
            return false;
        }
        return item1.Slug != null && item1.Slug.Equals(item2.Slug, StringComparison.OrdinalIgnoreCase);
    }

    public override void TryUpdate(Boxset fromDatabase, Boxset newValue)
    {
        fromDatabase.SortTitle = newValue.SortTitle;
        fromDatabase.Title = newValue.Title;
        fromDatabase.ImageUrl = newValue.ImageUrl;
        fromDatabase.Slug = newValue.Slug;

        if (fromDatabase.Release != null && newValue.Release != null)
        {
            this.releaseItemHandler.TryUpdate(fromDatabase.Release, newValue.Release);
        }
    }
}
