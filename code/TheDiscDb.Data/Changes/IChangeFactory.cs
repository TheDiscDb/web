namespace TheDiscDb.Data.Changes;

using System.Collections.Generic;

/// <summary>
/// Materialises an <see cref="IChange"/> from a stored
/// <see cref="TheDiscDb.Web.Data.EditSuggestionChange"/>'s type key + proposed JSON.
/// </summary>
public interface IChangeFactory
{
    IChange Create(string typeKey, string proposedJson);

    IReadOnlyCollection<string> RegisteredTypeKeys { get; }
}
