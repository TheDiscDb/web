using TheDiscDb.Client.Pages.Contribute;

namespace TheDiscDb.UnitTests.Client;

public class IdentifiedItemSubmissionTests
{
    [Test]
    public async Task GetSubmissionKind_WithDatabaseId_IsEdit()
    {
        // An already-identified item (episode or otherwise) carries a persisted database id and must
        // be updated in place. Routing it to Add is the regression that produced duplicate disc items.
        var kind = IdentifiedItemSubmission.GetSubmissionKind("DXl");

        await Assert.That(kind).IsEqualTo(ItemSubmissionKind.Edit);
    }

    [Test]
    public async Task GetSubmissionKind_WithoutDatabaseId_IsAdd()
    {
        var kind = IdentifiedItemSubmission.GetSubmissionKind(null);

        await Assert.That(kind).IsEqualTo(ItemSubmissionKind.Add);
    }
}
