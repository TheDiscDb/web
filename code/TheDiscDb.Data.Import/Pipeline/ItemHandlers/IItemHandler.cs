namespace TheDiscDb.Data.Import.Pipeline;

public interface IItemHandler<T>
{
    void TryUpdate(T fromDatabase, T newValue);
    bool IsMatch(T item1, T item2);
}
