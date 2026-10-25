using TheDiscDb.Components.Pages.Admin;

namespace TheDiscDb.UnitTests.Components.Pages;

public class EditSuggestionsSelectionTests
{
    [Test]
    public async Task RowClick_SelectsOnlyClickedRowAndKeepsItSelectedOnRepeatedClicks()
    {
        var selection = Create();
        selection.SetSelected(1, true, false);
        selection.SetSelected(2, true, false);

        selection.SelectRow(4, false, false);
        selection.SelectRow(4, false, false);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 4 });
    }

    [Test]
    public async Task ControlRowClick_TogglesOneRowWithoutClearingOtherSelections()
    {
        var selection = Create();
        selection.SelectRow(1, false, false);
        selection.SelectRow(3, true, false);
        selection.SelectRow(5, true, false);
        selection.SelectRow(3, true, false);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 1, 5 });
    }

    [Test]
    public async Task ShiftRowClick_SelectsInclusiveRangeAndPreservesUnrelatedSelections()
    {
        var selection = Create();
        selection.SetOrder([5, 1, 4, 2, 3]);
        selection.SelectRow(5, false, false);
        selection.SelectRow(1, true, false);
        selection.SelectRow(2, false, true);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 1, 2, 4, 5 });
    }

    [Test]
    public async Task RowAndCheckboxSelections_ShareTheSameRangeAnchor()
    {
        var selection = Create();
        selection.SetSelected(4, true, false);
        selection.SelectRow(2, false, true);
        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 2, 3, 4 });

        selection.SelectRow(1, false, false);
        selection.SetSelected(3, true, true);
        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 1, 2, 3 });
    }

    [Test]
    public async Task ShiftRowClickWithoutAnchor_UsesExistingCheckboxToggleBehavior()
    {
        var selection = Create();
        selection.SetSelected(3, true, false);
        selection.SetOrder([5, 4, 3, 2, 1]);
        selection.SelectRow(3, false, true);

        await Assert.That(selection.Selected).IsEmpty();
    }

    [Test]
    public async Task RowClickOnMissingItem_DoesNotClearExistingSelection()
    {
        var selection = Create();
        selection.SelectRow(2, false, false);
        selection.SelectRow(99, false, false);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 2 });
    }

    [Test]
    public async Task NormalSelection_TogglesOnlyTheRequestedRow()
    {
        var selection = Create();
        selection.SetSelected(2, true, false);
        selection.SetSelected(4, true, false);
        selection.SetSelected(2, false, false);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 4 });
    }

    [Test]
    public async Task ShiftSelection_IncludesBothEndpointsAndPreservesOtherSelections()
    {
        var selection = Create();
        selection.SetSelected(5, true, false);
        selection.SetSelected(1, true, false);
        selection.SetSelected(3, true, true);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 1, 2, 3, 5 });
    }

    [Test]
    public async Task ShiftSelection_WorksBackwardsAndSelectsEvenIfEndpointWasAlreadySelected()
    {
        var selection = Create();
        selection.SetSelected(1, true, false);
        selection.SetSelected(4, true, false);
        selection.SetSelected(1, false, true);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 1, 2, 3, 4 });
    }

    [Test]
    public async Task ShiftSelection_UsesDisplayedOrderNotIdOrder()
    {
        var selection = new SuggestionSelection();
        selection.SetOrder([5, 1, 4, 2, 3]);
        selection.SetSelected(1, true, false);
        selection.SetSelected(2, true, true);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 1, 2, 4 });
    }

    [Test]
    public async Task Sorting_ResetsAnchorButPreservesSelection()
    {
        var selection = Create();
        selection.SetSelected(1, true, false);
        selection.SetOrder([5, 4, 3, 2, 1]);
        selection.SetSelected(3, true, true);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 1, 3 });
    }

    [Test]
    public async Task SameOrderRefresh_DoesNotResetRangeAnchor()
    {
        var selection = Create();
        selection.SetSelected(1, true, false);
        selection.SetOrder([1, 2, 3, 4, 5]);
        selection.SetSelected(3, true, true);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 1, 2, 3 });
    }

    [Test]
    public async Task Clear_ResetsSelectionAndAnchorForFilterOrListChanges()
    {
        var selection = Create();
        selection.SetSelected(1, true, false);
        selection.Clear();
        selection.SetSelected(4, true, true);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 4 });
    }

    [Test]
    public async Task MissingRows_AreRemovedAndCannotBeSelected()
    {
        var selection = Create();
        selection.SetSelected(1, true, false);
        selection.SetOrder([2, 3]);
        selection.SetSelected(1, true, true);
        selection.SetSelected(3, true, true);

        await Assert.That(selection.Selected.Order().ToArray()).IsEquivalentTo(new[] { 3 });
    }

    private static SuggestionSelection Create()
    {
        var selection = new SuggestionSelection();
        selection.SetOrder([1, 2, 3, 4, 5]);
        return selection;
    }
}