using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheDiscDb.Client.Controls;

namespace TheDiscDb.UnitTests.Components.Controls;

public class JsonDiffTests
{
    private static readonly string[] DiscAddOnlyFields = ["contentHash", "globalDiscId", "fingerprint"];

    [Test]
    [Arguments("{}")]
    [Arguments("""{"contentHash":null,"globalDiscId":null,"fingerprint":null}""")]
    [Arguments("""{"contentHash":"","globalDiscId":"","fingerprint":""}""")]
    public async Task DiscIdentifiers_EmptyProposal_PreservesExistingValues(string proposed)
    {
        var html = await RenderAsync(
            """{"contentHash":"HASH","globalDiscId":"DISC-ID","fingerprint":"FINGERPRINT"}""",
            proposed,
            DiscAddOnlyFields);

        await Assert.That(html).DoesNotContain("diff-removed");
        await Assert.That(html).DoesNotContain("diff-added");
        await Assert.That(html).Contains("HASH");
        await Assert.That(html).Contains("DISC-ID");
        await Assert.That(html).Contains("FINGERPRINT");
    }

    [Test]
    public async Task DiscIdentifiers_PreservationDoesNotHideNameEdit()
    {
        var html = await RenderAsync(
            """{"name":"Old name","globalDiscId":"DISC-ID","fingerprint":"FINGERPRINT"}""",
            """{"name":"New name","globalDiscId":null,"fingerprint":null}""",
            DiscAddOnlyFields);

        await Assert.That(Count(html, "diff-removed")).IsEqualTo(1);
        await Assert.That(Count(html, "diff-added")).IsEqualTo(1);
        await Assert.That(Count(html, "diff-unchanged")).IsEqualTo(2);
        await Assert.That(html).Contains("Old name");
        await Assert.That(html).Contains("New name");
    }

    [Test]
    [Arguments("contentHash")]
    [Arguments("globalDiscId")]
    [Arguments("fingerprint")]
    public async Task DiscIdentifiers_NonemptyProposal_RemainsVisible(string key)
    {
        var html = await RenderAsync(
            $$"""{"{{key}}":"OLD"}""",
            $$"""{"{{key}}":"NEW"}""",
            DiscAddOnlyFields);

        await Assert.That(html).Contains("diff-removed");
        await Assert.That(html).Contains("diff-added");
        await Assert.That(html).Contains("OLD");
        await Assert.That(html).Contains("NEW");
    }

    [Test]
    public async Task DiscIdentifiers_NewValues_RemainVisible()
    {
        var html = await RenderAsync(
            """{"globalDiscId":null,"fingerprint":null}""",
            """{"globalDiscId":"DISC-ID","fingerprint":"FINGERPRINT"}""",
            DiscAddOnlyFields);

        await Assert.That(Count(html, "diff-added")).IsEqualTo(2);
        await Assert.That(html).Contains("DISC-ID");
        await Assert.That(html).Contains("FINGERPRINT");
    }

    [Test]
    public async Task OtherFields_ExplicitClear_RemainsVisible()
    {
        var html = await RenderAsync(
            """{"name":"Old name"}""",
            """{"name":null}""",
            DiscAddOnlyFields);

        await Assert.That(html).Contains("diff-removed");
        await Assert.That(html).Contains("diff-added");
        await Assert.That(html).Contains("null");
    }

    [Test]
    public async Task WithoutPreservation_IdentifierRemoval_RemainsVisible()
    {
        var html = await RenderAsync(
            """{"globalDiscId":"DISC-ID"}""",
            """{"globalDiscId":null}""");

        await Assert.That(html).Contains("diff-removed");
        await Assert.That(html).Contains("diff-added");
    }

    [Test]
    public async Task MissingProposal_StillShowsRemoval()
    {
        var html = await RenderAsync(
            """{"globalDiscId":"DISC-ID"}""",
            null,
            DiscAddOnlyFields);

        await Assert.That(html).Contains("diff-removed");
        await Assert.That(html).DoesNotContain("diff-unchanged");
    }

    [Test]
    public async Task ExcludedFields_StayHidden()
    {
        var html = await RenderAsync(
            """{"discSlug":"disc-one","globalDiscId":"DISC-ID"}""",
            """{"discSlug":"disc-one","globalDiscId":null}""",
            DiscAddOnlyFields,
            ["discSlug"]);

        await Assert.That(html).DoesNotContain("discSlug");
        await Assert.That(html).Contains("DISC-ID");
    }

    private static int Count(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static async Task<string> RenderAsync(
        string? original,
        string? proposed,
        IReadOnlyCollection<string>? preserveOnEmptyKeys = null,
        IReadOnlyCollection<string>? excludeKeys = null)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(JsonDiff.OriginalJson)] = original,
                [nameof(JsonDiff.ProposedJson)] = proposed,
                [nameof(JsonDiff.PreserveOnEmptyKeys)] = preserveOnEmptyKeys,
                [nameof(JsonDiff.ExcludeKeys)] = excludeKeys,
            });
            var result = await renderer.RenderComponentAsync<JsonDiff>(parameters);
            return result.ToHtmlString();
        });
    }
}
