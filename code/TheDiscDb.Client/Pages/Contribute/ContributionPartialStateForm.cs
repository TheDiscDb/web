namespace TheDiscDb.Client.Pages.Contribute;

using ContributionPartialStateInput = TheDiscDb.Client.Contributions.PartialStateInput;
using ContributionPartialStateReason = TheDiscDb.Client.Contributions.PartialStateReason;
using ContributionPartialStateSource = TheDiscDb.Client.Contributions.PartialStateSource;
using ContributionPartialStateType = TheDiscDb.Client.Contributions.PartialStateType;

public sealed class ContributionPartialStateForm
{
    public bool IsPartial { get; set; }
    public PartialStateReason Reason { get; set; }
    public string? Note { get; set; }

    public ContributionPartialStateInput? BuildInput(PartialStateType type)
    {
        if (!this.IsPartial)
        {
            return null;
        }

        return new ContributionPartialStateInput
        {
            Type = Enum.Parse<ContributionPartialStateType>(type.ToString()),
            Reason = Enum.Parse<ContributionPartialStateReason>(this.Reason.ToString()),
            Source = ContributionPartialStateSource.Declared,
            Note = string.IsNullOrWhiteSpace(this.Note) ? null : this.Note.Trim(),
        };
    }

    public bool IsValidFor(PartialStateType type)
    {
        if (!this.IsPartial || this.Reason == PartialStateReason.Unknown)
        {
            return false;
        }

        if (this.Reason == PartialStateReason.Other && string.IsNullOrWhiteSpace(this.Note))
        {
            return false;
        }

        try
        {
            new PartialState
            {
                Type = type,
                Reason = this.Reason,
                Source = PartialStateSource.Declared,
                Note = this.Note,
            }.Validate(type == PartialStateType.MissingDiscs
                ? PartialStateTarget.Release
                : PartialStateTarget.Disc);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Load(string? type, string? reason, string? note)
    {
        this.IsPartial = Enum.TryParse<PartialStateType>(type, out _);
        this.Reason = Enum.TryParse<PartialStateReason>(reason, out var parsedReason)
            ? parsedReason
            : PartialStateReason.Unknown;
        this.Note = note;
    }
}
