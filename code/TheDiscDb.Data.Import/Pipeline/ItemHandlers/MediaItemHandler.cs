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
        fromDatabase.Externalids = newValue.Externalids;
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
