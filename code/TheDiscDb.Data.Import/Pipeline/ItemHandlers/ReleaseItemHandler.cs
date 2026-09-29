namespace TheDiscDb.Data.Import.Pipeline;

using System;
using TheDiscDb.InputModels;

public class ReleaseItemHandler : ItemHandler<Release>
{
    private readonly IItemHandler<ReleaseDisc> discItemHandler;

    public ReleaseItemHandler(IItemHandler<ReleaseDisc> discItemHandler)
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

        if (newValue.Externalids != null)
        {
            // Merge in place to avoid creating a new ExternalIds row (and orphaning the
            // existing one) on every re-import. Only overwrite a non-empty incoming value so a
            // partial metadata refresh doesn't blank out an id set via the contribution flow.
            if (fromDatabase.Externalids == null)
            {
                fromDatabase.Externalids = newValue.Externalids;
            }
            else
            {
                foreach (var provider in TheDiscDb.InputModels.ExternalProviderCatalog.Providers)
                {
                    var incoming = TheDiscDb.InputModels.ExternalProviderCatalog.GetStoredId(newValue.Externalids, provider.Id);
                    if (!string.IsNullOrEmpty(incoming))
                    {
                        TheDiscDb.InputModels.ExternalProviderCatalog.SetStoredId(fromDatabase.Externalids, provider.Id, incoming);
                    }
                }
            }
        }

        HandleList(fromDatabase.Discs, newValue.Discs, this.discItemHandler);
    }
}
