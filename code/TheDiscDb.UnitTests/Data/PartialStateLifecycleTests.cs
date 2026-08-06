namespace TheDiscDb.UnitTests.Data;

using TheDiscDb.Web.Data;

public class PartialStateLifecycleTests
{
    [Test]
    public async Task ClearAutomaticUnidentified_ClearsOnlyAutomaticMarkerWithItems()
    {
        var automaticWithItem = new UserContributionDisc
        {
            Partial = PartialStateLifecycle.CreateAutomaticUnidentified(),
            Items = { new UserContributionDiscItem { Name = "Main feature" } },
        };
        var automaticWithoutItem = new UserContributionDisc
        {
            Partial = PartialStateLifecycle.CreateAutomaticUnidentified(),
        };
        var declared = new UserContributionDisc
        {
            Partial = new PartialState
            {
                Type = PartialStateType.Unidentified,
                Reason = PartialStateReason.LogsOnlyForOthersToComplete,
                Source = PartialStateSource.Declared,
            },
            Items = { new UserContributionDiscItem { Name = "Main feature" } },
        };

        PartialStateLifecycle.ClearAutomaticUnidentified(
            [automaticWithItem, automaticWithoutItem, declared]);

        await Assert.That(automaticWithItem.Partial).IsNull();
        await Assert.That(automaticWithoutItem.Partial).IsNotNull();
        await Assert.That(declared.Partial).IsNotNull();
    }
}
