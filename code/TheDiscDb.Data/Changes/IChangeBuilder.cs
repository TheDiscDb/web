namespace TheDiscDb.Data.Changes;

using System;

/// <summary>
/// Knows how to construct one specific <see cref="IChange"/> implementation from a
/// serialised <c>*Details</c> JSON payload. Registered with DI; consumed by
/// <see cref="IChangeFactory"/>.
/// </summary>
public interface IChangeBuilder
{
    /// <summary>The <see cref="IChange.TypeKey"/> this builder produces.</summary>
    string TypeKey { get; }

    /// <summary>The <c>*Details</c> record type the proposed JSON deserialises to.</summary>
    Type DetailsType { get; }

    /// <summary>
    /// Deserialises the proposed JSON to the strongly-typed Details record and
    /// constructs the matching <see cref="IChange"/>.
    /// </summary>
    /// <exception cref="InvalidChangeJsonException">
    /// Thrown when <paramref name="proposedJson"/> is null, empty, or cannot be
    /// deserialised to <see cref="DetailsType"/>.
    /// </exception>
    IChange Build(string proposedJson);
}
