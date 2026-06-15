namespace TheDiscDb.Data.Changes;

using System;
using System.Text.Json;

/// <summary>
/// Default <see cref="IChangeBuilder"/> implementation: deserialises the proposed
/// JSON to <typeparamref name="TDetails"/> and hands the result to a factory function
/// that produces the <see cref="IChange"/>. The same <see cref="JsonSerializerOptions"/>
/// used for deserialisation is threaded into the factory so that
/// <see cref="ChangeBase{TDetails}"/> implementations re-use them when deserialising
/// the original snapshot during validation.
/// </summary>
public sealed class ChangeBuilder<TDetails> : IChangeBuilder
    where TDetails : class
{
    private readonly Func<TDetails, JsonSerializerOptions, IChange> factory;
    private readonly JsonSerializerOptions jsonOptions;

    /// <summary>
    /// Construct a builder whose factory only needs the deserialised details.
    /// Use this overload for changes that do not derive from
    /// <see cref="ChangeBase{TDetails}"/> (i.e. they manage their own JSON options).
    /// </summary>
    public ChangeBuilder(string typeKey, Func<TDetails, IChange> factory, JsonSerializerOptions? jsonOptions = null)
        : this(typeKey, (d, _) => factory(d), jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(factory);
    }

    /// <summary>
    /// Construct a builder whose factory also receives the
    /// <see cref="JsonSerializerOptions"/> the builder used for deserialisation,
    /// so the constructed <see cref="IChange"/> can re-use them.
    /// </summary>
    public ChangeBuilder(string typeKey, Func<TDetails, JsonSerializerOptions, IChange> factory, JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeKey);
        ArgumentNullException.ThrowIfNull(factory);

        this.TypeKey = typeKey;
        this.factory = factory;
        this.jsonOptions = jsonOptions ?? DefaultJsonOptions;
    }

    public string TypeKey { get; }

    public Type DetailsType => typeof(TDetails);

    /// <summary>The serializer options this builder uses for proposed-JSON deserialisation.</summary>
    public JsonSerializerOptions JsonOptions => this.jsonOptions;

    public IChange Build(string proposedJson)
    {
        if (string.IsNullOrWhiteSpace(proposedJson))
        {
            throw new InvalidChangeJsonException(this.TypeKey, "Proposed JSON payload is empty.");
        }

        TDetails? details;
        try
        {
            details = JsonSerializer.Deserialize<TDetails>(proposedJson, this.jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidChangeJsonException(this.TypeKey, $"Failed to deserialise to {typeof(TDetails).Name}: {ex.Message}", ex);
        }

        if (details is null)
        {
            throw new InvalidChangeJsonException(this.TypeKey, $"Deserialised payload was null for {typeof(TDetails).Name}.");
        }

        return this.factory(details, this.jsonOptions);
    }

    internal static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);
}
