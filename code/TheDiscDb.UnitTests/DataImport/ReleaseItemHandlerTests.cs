namespace TheDiscDb.UnitTests.DataImport;

using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.InputModels;

public class ReleaseItemHandlerTests
{
    private sealed class NoOpReleaseDiscHandler : IItemHandler<ReleaseDisc>
    {
        public bool IsMatch(ReleaseDisc item1, ReleaseDisc item2) => false;
        public void TryUpdate(ReleaseDisc fromDatabase, ReleaseDisc newValue) { }
    }

    private static ReleaseItemHandler CreateHandler() => new(new NoOpReleaseDiscHandler());

    [Test]
    public async Task TryUpdate_SetsExternalIds_WhenDatabaseHasNone()
    {
        var handler = CreateHandler();
        var fromDatabase = new Release();
        var newValue = new Release
        {
            Externalids = new ExternalIds { BlurayCom = "263678" }
        };

        handler.TryUpdate(fromDatabase, newValue);

        await Assert.That(fromDatabase.Externalids).IsNotNull();
        await Assert.That(fromDatabase.Externalids!.BlurayCom).IsEqualTo("263678");
    }

    [Test]
    public async Task TryUpdate_MergesInPlace_PreservingExistingRowAndUnsetProviders()
    {
        var handler = CreateHandler();
        var existingExternalIds = new ExternalIds { BlurayCom = "111", DvdCompare = "222" };
        var fromDatabase = new Release { Externalids = existingExternalIds };
        var newValue = new Release
        {
            // Only bluray provided on re-import; dvdcompare must survive untouched.
            Externalids = new ExternalIds { BlurayCom = "999" }
        };

        handler.TryUpdate(fromDatabase, newValue);

        // Same row instance is reused (merged in place), not replaced.
        await Assert.That(fromDatabase.Externalids).IsSameReferenceAs(existingExternalIds);
        await Assert.That(fromDatabase.Externalids!.BlurayCom).IsEqualTo("999");
        await Assert.That(fromDatabase.Externalids!.DvdCompare).IsEqualTo("222");
    }

    [Test]
    public async Task TryUpdate_LeavesExternalIds_WhenNewValueHasNone()
    {
        var handler = CreateHandler();
        var fromDatabase = new Release();
        var newValue = new Release { Externalids = null };

        handler.TryUpdate(fromDatabase, newValue);

        await Assert.That(fromDatabase.Externalids).IsNull();
    }
}
