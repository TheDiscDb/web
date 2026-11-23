using TheDiscDb.Client.Contributions;
using TheDiscDb.Client.Controls;

namespace TheDiscDb.UnitTests.Client;

public class MessageTimelineTests
{
    [Test]
    public async Task SenderLabels_DistinguishContributorAndOtherAdminsFromViewer()
    {
        var message = new ConversationMessage(1, "owner", "", "Reply", false, DateTimeOffset.UtcNow, UserMessageType.UserMessage);
        await Assert.That(MessageTimeline.GetSenderLabel(message, "admin-a")).IsEqualTo("Contributor");
        await Assert.That(MessageTimeline.GetSenderLabel(message with { FromUserId = "admin-b", Type = UserMessageType.AdminMessage }, "admin-a")).IsEqualTo("Admin");
        await Assert.That(MessageTimeline.GetSenderLabel(message with { FromUserId = "admin-a", Type = UserMessageType.AdminMessage }, "admin-a")).IsEqualTo("You");
    }
}