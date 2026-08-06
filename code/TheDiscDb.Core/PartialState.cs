namespace TheDiscDb;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public sealed record class PartialState
{
    public PartialStateType Type { get; set; }
    public PartialStateReason Reason { get; set; }
    public PartialStateSource Source { get; set; }
    public string? Note { get; set; }

    public void Validate(PartialStateTarget target)
    {
        if (!Enum.IsDefined(this.Type) || this.Type == PartialStateType.Unknown)
        {
            throw new InvalidOperationException($"Unknown partial state type '{this.Type}'.");
        }

        if (!Enum.IsDefined(this.Reason) || this.Reason == PartialStateReason.Unknown)
        {
            throw new InvalidOperationException($"Unknown partial state reason '{this.Reason}'.");
        }

        if (!Enum.IsDefined(this.Source) || this.Source == PartialStateSource.Unknown)
        {
            throw new InvalidOperationException($"Unknown partial state source '{this.Source}'.");
        }

        var expectedTarget = this.Type == PartialStateType.MissingDiscs
            ? PartialStateTarget.Release
            : PartialStateTarget.Disc;
        if (target != expectedTarget)
        {
            throw new InvalidOperationException(
                $"Partial state type '{this.Type}' is only valid for a {expectedTarget.ToString().ToLowerInvariant()}.");
        }

        if (!ReasonsByType[this.Type].Contains(this.Reason))
        {
            throw new InvalidOperationException(
                $"Partial state reason '{this.Reason}' is not valid for type '{this.Type}'.");
        }

        var isAutomatic = this.Source == PartialStateSource.Automatic;
        var isNeedsIdentification = this.Reason == PartialStateReason.NeedsIdentification;
        if (isAutomatic != isNeedsIdentification)
        {
            throw new InvalidOperationException(
                $"Source '{PartialStateSource.Automatic}' and reason '{PartialStateReason.NeedsIdentification}' must be used together.");
        }

        if (this.Reason == PartialStateReason.Other && string.IsNullOrWhiteSpace(this.Note))
        {
            throw new InvalidOperationException("A note is required when the partial state reason is Other.");
        }
    }

    private static readonly IReadOnlyDictionary<PartialStateType, HashSet<PartialStateReason>> ReasonsByType =
        new Dictionary<PartialStateType, HashSet<PartialStateReason>>
        {
            [PartialStateType.MissingDiscs] =
            [
                PartialStateReason.OnlyOwnsSomeDiscs,
                PartialStateReason.DiscMissing,
                PartialStateReason.DiscDamagedOrUnreadable,
                PartialStateReason.Other,
            ],
            [PartialStateType.PartiallyIdentified] =
            [
                PartialStateReason.OnlyIdentifyingMainFeature,
                PartialStateReason.ExtrasOutOfScope,
                PartialStateReason.Other,
            ],
            [PartialStateType.Unidentified] =
            [
                PartialStateReason.LogsOnlyForOthersToComplete,
                PartialStateReason.NeedsIdentification,
                PartialStateReason.Other,
            ],
        };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartialStateType
{
    Unknown,
    MissingDiscs,
    PartiallyIdentified,
    Unidentified,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartialStateReason
{
    Unknown,
    OnlyOwnsSomeDiscs,
    DiscMissing,
    DiscDamagedOrUnreadable,
    OnlyIdentifyingMainFeature,
    ExtrasOutOfScope,
    LogsOnlyForOthersToComplete,
    NeedsIdentification,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartialStateSource
{
    Unknown,
    Declared,
    Automatic,
}

public enum PartialStateTarget
{
    Disc,
    Release,
}
