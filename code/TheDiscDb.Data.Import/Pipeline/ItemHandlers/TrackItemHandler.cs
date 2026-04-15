namespace TheDiscDb.Data.Import.Pipeline;

using TheDiscDb.InputModels;

public class TrackItemHandler : ItemHandler<Track>
{
    public override bool IsMatch(Track d1, Track d2)
    {
        if (d1 == null || d2 == null)
        {
            return false;
        }

        return d1.Index == d2.Index &&
            d1.Name == d2.Name &&
            d1.Type == d2.Type &&
            d1.Resolution == d2.Resolution &&
            d1.AspectRatio == d2.AspectRatio &&
            d1.AudioType == d2.AudioType &&
            d1.LanguageCode == d2.LanguageCode &&
            d1.Language == d2.Language;
    }

    public override void TryUpdate(Track fromDatabase, Track newValue)
    {
        fromDatabase.Index = newValue.Index;
        fromDatabase.Name = newValue.Name;
        fromDatabase.Type = newValue.Type;
        fromDatabase.Resolution = newValue.Resolution;
        fromDatabase.AspectRatio = newValue.AspectRatio;
        fromDatabase.AudioType = newValue.AudioType;
        fromDatabase.LanguageCode = newValue.LanguageCode;
        fromDatabase.Language = newValue.Language;
    }
}
