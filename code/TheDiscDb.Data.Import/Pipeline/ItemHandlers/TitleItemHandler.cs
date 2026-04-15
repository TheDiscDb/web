namespace TheDiscDb.Data.Import.Pipeline;

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
            AnsiConsole.WriteLine("Warning: Title mapped item '{0}' found in db but not in update data", fromDatabase.SourceFile ?? string.Empty);
            fromDatabase.Item = null;
        }

        this.HandleList(fromDatabase.Tracks, newValue.Tracks, this.trackItemHandler);
    }
}
