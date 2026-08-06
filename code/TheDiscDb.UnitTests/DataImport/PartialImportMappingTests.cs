namespace TheDiscDb.UnitTests.DataImport;

using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.InputModels;

public class PartialImportMappingTests
{
    [Test]
    public async Task ReleaseItemHandler_CopiesPartialState()
    {
        var handler = new ReleaseItemHandler(new NoOpItemHandler<ReleaseDisc>());
        var expected = new PartialState
        {
            Type = PartialStateType.MissingDiscs,
            Reason = PartialStateReason.OnlyOwnsSomeDiscs,
            Source = PartialStateSource.Declared,
        };
        var existing = new Release();
        var imported = new Release { Partial = expected };

        handler.TryUpdate(existing, imported);

        await Assert.That(existing.Partial).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task DiscItemHandler_CopiesPartialState()
    {
        var handler = new DiscItemHandler(new NoOpItemHandler<Title>());
        var expected = new PartialState
        {
            Type = PartialStateType.PartiallyIdentified,
            Reason = PartialStateReason.ExtrasOutOfScope,
            Source = PartialStateSource.Declared,
        };
        var existing = new Disc();
        var imported = new Disc { Partial = expected };

        handler.TryUpdate(existing, imported);

        await Assert.That(existing.Partial).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task DiscItemHandler_InvalidPartialState_Throws()
    {
        var handler = new DiscItemHandler(new NoOpItemHandler<Title>());
        var imported = new Disc
        {
            Partial = new PartialState
            {
                Type = PartialStateType.MissingDiscs,
                Reason = PartialStateReason.DiscMissing,
                Source = PartialStateSource.Declared,
            },
        };

        await Assert.That(() => handler.TryUpdate(new Disc(), imported))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BoxsetItemHandler_CopiesAssociatedReleasePartialState()
    {
        var releaseHandler = new ReleaseItemHandler(new NoOpItemHandler<ReleaseDisc>());
        var handler = new BoxsetItemHandler(releaseHandler);
        var expected = new PartialState
        {
            Type = PartialStateType.MissingDiscs,
            Reason = PartialStateReason.DiscDamagedOrUnreadable,
            Source = PartialStateSource.Declared,
        };
        var existing = new Boxset { Release = new Release() };
        var imported = new Boxset { Release = new Release { Partial = expected } };

        handler.TryUpdate(existing, imported);

        await Assert.That(existing.Release.Partial).IsSameReferenceAs(expected);
    }

    private sealed class NoOpItemHandler<T> : IItemHandler<T>
    {
        public bool IsMatch(T item1, T item2) => false;

        public void TryUpdate(T fromDatabase, T newValue)
        {
        }
    }
}
