using TheDiscDb.Naming;

namespace TheDiscDb.UnitTests.Naming;

public class ItemTypeNamesTests
{
    [Test]
    [Arguments(ItemTypeNames.Extra)]
    [Arguments(ItemTypeNames.Other)]
    [Arguments(ItemTypeNames.Interview)]
    [Arguments(ItemTypeNames.Featurette)]
    [Arguments(ItemTypeNames.Scene)]
    [Arguments(ItemTypeNames.Music)]
    [Arguments(ItemTypeNames.Short)]
    public async Task IsExtra_ReturnsTrueForExtraFamily(string type)
    {
        await Assert.That(ItemTypeNames.IsExtra(type)).IsTrue();
    }

    [Test]
    [Arguments("extra")]
    [Arguments("EXTRA")]
    [Arguments("interview")]
    [Arguments("FEATURETTE")]
    public async Task IsExtra_IsCaseInsensitive(string type)
    {
        await Assert.That(ItemTypeNames.IsExtra(type)).IsTrue();
    }

    [Test]
    [Arguments(ItemTypeNames.MainMovie)]
    [Arguments(ItemTypeNames.Episode)]
    [Arguments(ItemTypeNames.Trailer)]
    [Arguments(ItemTypeNames.DeletedScene)]
    [Arguments("Bogus")]
    public async Task IsExtra_ReturnsFalseForNonExtra(string type)
    {
        await Assert.That(ItemTypeNames.IsExtra(type)).IsFalse();
    }

    [Test]
    public async Task IsExtra_NullOrEmpty_ReturnsFalse()
    {
        await Assert.That(ItemTypeNames.IsExtra(null)).IsFalse();
        await Assert.That(ItemTypeNames.IsExtra(string.Empty)).IsFalse();
    }

    [Test]
    public async Task ExtraTypes_ContainsExtraAndAllSubCategories()
    {
        await Assert.That(ItemTypeNames.ExtraTypes).Contains(ItemTypeNames.Extra);
        await Assert.That(ItemTypeNames.ExtraTypes).Contains(ItemTypeNames.Other);
        await Assert.That(ItemTypeNames.ExtraTypes).Contains(ItemTypeNames.Interview);
        await Assert.That(ItemTypeNames.ExtraTypes).Contains(ItemTypeNames.Featurette);
        await Assert.That(ItemTypeNames.ExtraTypes).Contains(ItemTypeNames.Scene);
        await Assert.That(ItemTypeNames.ExtraTypes).Contains(ItemTypeNames.Music);
        await Assert.That(ItemTypeNames.ExtraTypes).Contains(ItemTypeNames.Short);
    }
}
