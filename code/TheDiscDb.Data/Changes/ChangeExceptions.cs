namespace TheDiscDb.Data.Changes;

using System;

public class UnknownChangeTypeException : Exception
{
    public UnknownChangeTypeException(string typeKey)
        : base($"No change builder is registered for type key '{typeKey}'.")
    {
        this.TypeKey = typeKey;
    }

    public string TypeKey { get; }
}

public class InvalidChangeJsonException : Exception
{
    public InvalidChangeJsonException(string typeKey, string message)
        : base($"Invalid change JSON for type key '{typeKey}': {message}")
    {
        this.TypeKey = typeKey;
    }

    public InvalidChangeJsonException(string typeKey, string message, Exception inner)
        : base($"Invalid change JSON for type key '{typeKey}': {message}", inner)
    {
        this.TypeKey = typeKey;
    }

    public string TypeKey { get; }
}

public class DuplicateChangeBuilderException : Exception
{
    public DuplicateChangeBuilderException(string typeKey)
        : base($"More than one change builder is registered for type key '{typeKey}'. Each type key must be unique.")
    {
        this.TypeKey = typeKey;
    }

    public string TypeKey { get; }
}

/// <summary>
/// Thrown by <see cref="IChange.ApplyAsync"/> when re-validation inside the apply
/// transaction detects that the target entity has drifted since the original
/// validation pass. The review service should surface this as a conflict that
/// requires admin re-review rather than a hard failure.
/// </summary>
public class ChangeApplyConflictException : Exception
{
    public ChangeApplyConflictException(string typeKey, string reason)
        : base($"Change '{typeKey}' could not be applied: {reason}")
    {
        this.TypeKey = typeKey;
        this.Reason = reason;
    }

    public string TypeKey { get; }

    public string Reason { get; }
}
