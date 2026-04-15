namespace TheDiscDb.Data.Import.Pipeline;

using System.Collections.Generic;
using System.Linq;

public abstract class ItemHandler<T> : IItemHandler<T>
{
    public abstract bool IsMatch(T item1, T item2);
    public abstract void TryUpdate(T fromDatabase, T newValue);

    /// <summary>Handle updating a sub list of items</summary>
    protected void HandleList<TItem>(ICollection<TItem> fromDatabase, ICollection<TItem> newValue, IItemHandler<TItem> handler)
    {
        if (newValue.Count == fromDatabase.Count)
        {
            for (int i = 0; i < fromDatabase.Count; i++)
            {
                handler.TryUpdate(fromDatabase.ElementAt(i), newValue.ElementAt(i));
            }
        }
        else
        {
            if (fromDatabase.Count == 0 && newValue.Any())
            {
                foreach (var newItem in newValue)
                {
                    fromDatabase.Add(newItem);
                }
            }
            else
            {
                foreach (var itemFromDisc in newValue)
                {
                    var match = fromDatabase.FirstOrDefault(d => handler.IsMatch(d, itemFromDisc));
                    if (match != null)
                    {
                        handler.TryUpdate(itemFromDisc, match);
                    }
                    else
                    {
                        fromDatabase.Add(itemFromDisc);
                    }
                }

                List<TItem> itemsToRemove = new List<TItem>();
                foreach (var itemFromDatabase in fromDatabase)
                {
                    var match = newValue.FirstOrDefault(d => handler.IsMatch(d, itemFromDatabase));
                    if (match == null)
                    {
                        itemsToRemove.Add(itemFromDatabase);
                    }
                }

                foreach (var itemToRemove in itemsToRemove)
                {
                    fromDatabase.Remove(itemToRemove);
                }
            }
        }
    }
}
