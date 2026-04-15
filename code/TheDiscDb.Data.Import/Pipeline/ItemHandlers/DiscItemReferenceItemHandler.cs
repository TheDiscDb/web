namespace TheDiscDb.Data.Import.Pipeline;

using System;
using System.Linq;
using TheDiscDb.InputModels;

public class DiscItemReferenceItemHandler : ItemHandler<DiscItemReference>
{
    public override bool IsMatch(DiscItemReference d1, DiscItemReference d2)
    {
        throw new NotImplementedException();
    }

    public override void TryUpdate(DiscItemReference fromDatabase, DiscItemReference newValue)
    {
        fromDatabase.Description = newValue.Description;
        fromDatabase.Episode = newValue.Episode;
        fromDatabase.Season = newValue.Season;
        fromDatabase.Title = newValue.Title;
        fromDatabase.Type = newValue.Type;

        if (fromDatabase.Chapters.Count == newValue.Chapters.Count)
        {
            for (int i = 0; i < newValue.Chapters.Count; i++)
            {
                fromDatabase.Chapters.ElementAt(i).Index = newValue.Chapters.ElementAt(i).Index;
                fromDatabase.Chapters.ElementAt(i).Title = newValue.Chapters.ElementAt(i).Title;
            }
        }
    }
}
