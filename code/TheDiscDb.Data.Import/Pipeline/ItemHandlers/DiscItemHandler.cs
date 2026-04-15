namespace TheDiscDb.Data.Import.Pipeline;

using System;
using TheDiscDb.InputModels;

public class DiscItemHandler : ItemHandler<Disc>
{
    private readonly IItemHandler<Title> titleItemHandler;

    public DiscItemHandler(IItemHandler<Title> titleItemHandler)
    {
        this.titleItemHandler = titleItemHandler;
    }

    public override bool IsMatch(Disc d1, Disc d2)
    {
        if (d1 == null || d2 == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(d1.ContentHash) && !string.IsNullOrEmpty(d2.ContentHash) && d1.ContentHash == d2.ContentHash)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(d1.Slug) && !string.IsNullOrEmpty(d2.Slug) && d1.Slug.Equals(d2.Slug, StringComparison.OrdinalIgnoreCase) && d1.Format == d2.Format)
        {
            return true;
        }

        return false;
    }

    public override void TryUpdate(Disc fromDatabase, Disc newValue)
    {
        fromDatabase.ContentHash = newValue.ContentHash;
        fromDatabase.Format = newValue.Format;
        fromDatabase.Index = newValue.Index;
        fromDatabase.Name = newValue.Name;
        fromDatabase.Slug = newValue.Slug;

        this.HandleList(fromDatabase.Titles, newValue.Titles, this.titleItemHandler);
    }
}
