namespace TheDiscDb.Data.Import.Pipeline;

using System.Linq;
using Spectre.Console;
using TheDiscDb.InputModels;

public class TitleItemHandler : ItemHandler<Title>
{
    private readonly IItemHandler<Track> trackItemHandler;
    private readonly IItemHandler<DiscItemReference> discItemReferenceHandler;

    public TitleItemHandler(IItemHandler<Track> trackItemHandler, IItemHandler<DiscItemReference> discItemReferenceHandler)
    {
        this.trackItemHandler = trackItemHandler;
        this.discItemReferenceHandler = discItemReferenceHandler;
    }

    public override bool IsMatch(Title d1, Title d2)
    {
        if (d1 == null || d2 == null)
        {
            return false;
        }

        return d1.SourceFile == d2.SourceFile &&
            d1.SegmentMap == d2.SegmentMap &&
            d1.Duration == d2.Duration &&
            d1.Size == d2.Size &&
            d1.DisplaySize == d2.DisplaySize;
    }

    public override void TryUpdate(Title fromDatabase, Title newValue)
    {
        bool clearsExistingMapping = fromDatabase.Item != null && newValue.Item == null;
        if (clearsExistingMapping)
        {
            AnsiConsole.WriteLine(BuildMissingItemWarning(fromDatabase, newValue));
        }

        fromDatabase.Comment = newValue.Comment;
        fromDatabase.SourceFile = newValue.SourceFile;
        fromDatabase.SegmentMap = newValue.SegmentMap;
        fromDatabase.DisplaySize = newValue.DisplaySize;
        fromDatabase.Size = newValue.Size;
        fromDatabase.Duration = newValue.Duration;
        fromDatabase.Index = newValue.Index;
        if (newValue.Item != null && fromDatabase.Item != null)
        {
            this.discItemReferenceHandler.TryUpdate(fromDatabase.Item, newValue.Item);
        }
        else if (newValue.Item != null && fromDatabase.Item == null)
        {
            fromDatabase.Item = newValue.Item;
        }
        else if (fromDatabase.Item != null && newValue.Item == null)
        {
            fromDatabase.Item = null;
        }

        this.HandleList(fromDatabase.Tracks, newValue.Tracks, this.trackItemHandler);
    }

    internal static string BuildMissingItemWarning(Title databaseTitle, Title updateTitle)
    {
        var item = databaseTitle.Item;
        string mappedItem = string.Join(
            ", ",
            new[]
            {
                item?.Type,
                item?.Title,
                string.IsNullOrWhiteSpace(item?.Season) ? null : $"season {item.Season}",
                string.IsNullOrWhiteSpace(item?.Episode) ? null : $"episode {item.Episode}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return $"Warning: Title mapping found in database but not in update data; clearing mapping. " +
            $"Disc: '{databaseTitle.Disc?.Name ?? string.Empty}' (slug '{databaseTitle.Disc?.Slug ?? string.Empty}', " +
            $"format '{databaseTitle.Disc?.Format ?? string.Empty}', content hash '{databaseTitle.Disc?.ContentHash ?? string.Empty}'). " +
            $"Database title: index {databaseTitle.Index}, source file '{databaseTitle.SourceFile ?? string.Empty}', " +
            $"segment map '{databaseTitle.SegmentMap ?? string.Empty}', duration '{databaseTitle.Duration ?? string.Empty}', " +
            $"comment '{databaseTitle.Comment ?? string.Empty}', mapped item '{mappedItem}'. " +
            $"Update title: index {updateTitle.Index}, source file '{updateTitle.SourceFile ?? string.Empty}', " +
            $"segment map '{updateTitle.SegmentMap ?? string.Empty}', duration '{updateTitle.Duration ?? string.Empty}', " +
            $"comment '{updateTitle.Comment ?? string.Empty}'";
    }
}
