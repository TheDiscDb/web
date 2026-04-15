namespace TheDiscDb.Data.Import.Pipeline;

using System;
using TheDiscDb.InputModels;

public class ReleaseItemHandler : ItemHandler<Release>
{
    private readonly IItemHandler<Disc> discItemHandler;

    public ReleaseItemHandler(IItemHandler<Disc> discItemHandler)
    {
        this.discItemHandler = discItemHandler ?? throw new ArgumentNullException(nameof(discItemHandler));
    }

    public override bool IsMatch(Release item1, Release item2)
    {
        if (item1 == null || item2 == null)
        {
            return false;
        }

        return item1.Slug != null && item1.Slug.Equals(item2.Slug, StringComparison.OrdinalIgnoreCase);
    }

    public override async void TryUpdate(Release fromDatabase, Release newValue)
    {
        fromDatabase.DateAdded = newValue.DateAdded;
        fromDatabase.ReleaseDate = newValue.ReleaseDate;
        fromDatabase.Title = newValue.Title;
        fromDatabase.Locale = newValue.Locale;
        fromDatabase.Asin = newValue.Asin;
        fromDatabase.ImageUrl = newValue.ImageUrl;
        fromDatabase.Isbn = newValue.Isbn;
        fromDatabase.RegionCode = newValue.RegionCode;
        fromDatabase.Upc = newValue.Upc;
        fromDatabase.Year = newValue.Year;

        HandleList(fromDatabase.Discs, newValue.Discs, this.discItemHandler);
    }
}
