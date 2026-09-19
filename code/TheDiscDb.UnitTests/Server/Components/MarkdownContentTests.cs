namespace TheDiscDb.UnitTests.Server.Components;

using TheDiscDb.Components.Controls;

public class MarkdownContentTests
{
    [Test]
    public async Task Render_DisablesHtmlAndRemovesUnsafeUrlSchemes()
    {
        var html = MarkdownContent.Render(
            """<script>alert(1)</script> [unsafe](javascript:alert(2)) [safe](https://example.com)""");

        await Assert.That(html).DoesNotContain("<script>");
        await Assert.That(html).DoesNotContain("javascript:");
        await Assert.That(html).Contains("href=\"https://example.com\"");
    }
}
