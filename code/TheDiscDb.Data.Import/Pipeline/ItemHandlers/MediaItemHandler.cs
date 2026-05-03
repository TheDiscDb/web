namespace TheDiscDb.Data.Import.Pipeline;

using System;
using TheDiscDb.InputModels;

public class MediaItemHandler : ItemHandler<MediaItem>
{
    private readonly IItemHandler<Release> releaseItemHandler;

    public MediaItemHandler(IItemHandler<Release> releaseItemHandler)
    {
        this.releaseItemHandler = releaseItemHandler ?? throw new ArgumentNullException(nameof(releaseItemHandler));
    }

    public override bool IsMatch(MediaItem item1, MediaItem item2)
    {
        throw new NotImplementedException();
    }

    public override void TryUpdate(MediaItem fromDatabase, MediaItem newValue)
    {
        fromDatabase.Title = newValue.Title;
        fromDatabase.SortTitle = newValue.SortTitle;
        fromDatabase.FullTitle = newValue.FullTitle;
        if (newValue.Externalids != null)
        {
            // Merge in place to avoid creating a new ExternalIds row (and orphaning
            // the existing one) on every re-import. Only overwrite a non-empty
            // incoming value so a partial metadata refresh doesn't blank out an id.
            if (fromDatabase.Externalids == null)
            {
                fromDatabase.Externalids = newValue.Externalids;
            }
            else
            {
                if (!string.IsNullOrEmpty(newValue.Externalids.Tmdb))
                {
                    fromDatabase.Externalids.Tmdb = newValue.Externalids.Tmdb;
                }
                if (!string.IsNullOrEmpty(newValue.Externalids.Imdb))
                {
                    fromDatabase.Externalids.Imdb = newValue.Externalids.Imdb;
                }
                if (!string.IsNullOrEmpty(newValue.Externalids.Tvdb))
                {
                    fromDatabase.Externalids.Tvdb = newValue.Externalids.Tvdb;
                }
            }
        }
        fromDatabase.Slug = newValue.Slug;
        fromDatabase.Year = newValue.Year;
        fromDatabase.ImageUrl = newValue.ImageUrl;
        fromDatabase.ReleaseDate = newValue.ReleaseDate;
        fromDatabase.LatestReleaseDate = newValue.LatestReleaseDate;
        fromDatabase.DateAdded = newValue.DateAdded;

        fromDatabase.ContentRating = newValue.ContentRating;
        fromDatabase.Directors = newValue.Directors;
        fromDatabase.Stars = newValue.Stars;
        fromDatabase.Genres = newValue.Genres;
        fromDatabase.Plot = newValue.Plot;
        fromDatabase.Tagline = newValue.Tagline;
        fromDatabase.Runtime = newValue.Runtime;
        fromDatabase.RuntimeMinutes = newValue.RuntimeMinutes;
        fromDatabase.Writers = newValue.Writers;

        this.HandleList(fromDatabase.Releases, newValue.Releases, this.releaseItemHandler);
    }
}
