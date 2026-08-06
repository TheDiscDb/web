namespace TheDiscDb.UnitTests.Core;

public class PartialStateValidationTests
{
    [Test]
    public async Task Validate_MissingDiscReleaseState_IsValid()
    {
        var partial = new PartialState
        {
            Type = PartialStateType.MissingDiscs,
            Reason = PartialStateReason.DiscMissing,
            Source = PartialStateSource.Declared,
        };

        partial.Validate(PartialStateTarget.Release);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Validate_DiscReasonOnRelease_Throws()
    {
        var partial = new PartialState
        {
            Type = PartialStateType.MissingDiscs,
            Reason = PartialStateReason.ExtrasOutOfScope,
            Source = PartialStateSource.Declared,
        };

        await Assert.That(() => partial.Validate(PartialStateTarget.Release))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Validate_ReleaseTypeOnDisc_Throws()
    {
        var partial = new PartialState
        {
            Type = PartialStateType.MissingDiscs,
            Reason = PartialStateReason.DiscMissing,
            Source = PartialStateSource.Declared,
        };

        await Assert.That(() => partial.Validate(PartialStateTarget.Disc))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Validate_AutomatedNeedsIdentification_IsValid()
    {
        var partial = new PartialState
        {
            Type = PartialStateType.Unidentified,
            Reason = PartialStateReason.NeedsIdentification,
            Source = PartialStateSource.Automatic,
        };

        partial.Validate(PartialStateTarget.Disc);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Validate_AutomatedSourceWithDeclaredReason_Throws()
    {
        var partial = new PartialState
        {
            Type = PartialStateType.Unidentified,
            Reason = PartialStateReason.LogsOnlyForOthersToComplete,
            Source = PartialStateSource.Automatic,
        };

        await Assert.That(() => partial.Validate(PartialStateTarget.Disc))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Validate_OtherWithoutNote_Throws()
    {
        var partial = new PartialState
        {
            Type = PartialStateType.PartiallyIdentified,
            Reason = PartialStateReason.Other,
            Source = PartialStateSource.Declared,
        };

        await Assert.That(() => partial.Validate(PartialStateTarget.Disc))
            .Throws<InvalidOperationException>();
    }
}
