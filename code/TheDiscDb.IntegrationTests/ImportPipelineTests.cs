using System.Text;
using System.Text.Json;

namespace TheDiscDb.IntegrationTests;

/// <summary>
/// Verifies the import pipeline (DataSeeder) produces expected data.
///
/// NOTE: The DataSeeder uses two phases — Phase 1 seeds ~10 items per type
/// and marks the app healthy, Phase 2 seeds the rest in background. Tests
/// run after Phase 1 completes, so they may see partial data. Assertions
/// are kept loose (> 0 rather than exact counts) to accommodate this.
///
/// NOTE: ExceptionHandlingMiddleware catches import failures per-item and
/// continues. The app can start healthy even if some items failed. These
/// tests verify expected data IS present (positive signal) rather than
/// asserting zero failures (which we can't observe via GraphQL).
/// </summary>
[ClassDataSource<AspireAppFixture>(Shared = SharedType.PerTestSession)]
public class ImportPipelineTests(AspireAppFixture fixture)
{
    private HttpClient Client => fixture.HttpClient;

    private async Task<JsonDocument> ExecuteGraphQLAsync(string query)
    {
        var request = new { query };
        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await Client.PostAsync("/graphql", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    [Test]
    public async Task DataSeeder_Phase1_MediaItemsSeeded()
    {
        // Phase 1 seeds at least InitialSeedCount (default 10) items.
        // If this fails, the pipeline is fundamentally broken.
        using var doc = await ExecuteGraphQLAsync(
            "{ mediaItems(first: 1) { nodes { title slug } } }");

        var nodes = doc.RootElement
            .GetProperty("data")
            .GetProperty("mediaItems")
            .GetProperty("nodes");

        await Assert.That(nodes.GetArrayLength()).IsGreaterThan(0);
    }

    [Test]
    public async Task Releases_HaveContributors()
    {
        // Most releases in the data repo have a "lfoust" contributor.
        // After Phase 1, at least some seeded items should have contributors.
        using var doc = await ExecuteGraphQLAsync("""
            {
                mediaItems(
                    first: 5,
                    where: { releases: { some: { contributors: { some: { name: { neq: null } } } } } }
                ) {
                    nodes {
                        title
                        slug
                        releases {
                            slug
                            contributors {
                                name
                            }
                        }
                    }
                }
            }
            """);

        var nodes = doc.RootElement
            .GetProperty("data")
            .GetProperty("mediaItems")
            .GetProperty("nodes");

        await Assert.That(nodes.GetArrayLength()).IsGreaterThan(0);
    }

    [Test]
    public async Task SharedContributor_ImportedAcrossMultipleMediaItems()
    {
        // "lfoust" appears across many release.json files. When the pipeline's
        // dedup logic is broken, one of two things happens:
        //   1. Duplicate key exception on Contributors table — the failing item
        //      is skipped, reducing the count of items with this contributor
        //   2. Change tracker pollution cascades — subsequent items sharing the
        //      contributor also fail, dramatically reducing the count
        //
        // This test acts as a canary: if the count drops significantly from
        // historical norms, something is wrong with contributor handling.
        // It does NOT directly verify entity-level uniqueness (that would
        // require direct DB access).
        using var doc = await ExecuteGraphQLAsync("""
            {
                mediaItems(
                    first: 10,
                    where: { releases: { some: { contributors: { some: { name: { eq: "lfoust" } } } } } }
                ) {
                    nodes {
                        slug
                    }
                }
            }
            """);

        var nodes = doc.RootElement
            .GetProperty("data")
            .GetProperty("mediaItems")
            .GetProperty("nodes");

        // After Phase 1 (~10 items per type), at least 2 should have "lfoust"
        await Assert.That(nodes.GetArrayLength()).IsGreaterThan(1);
    }

    [Test]
    public async Task MediaItems_HaveGroups()
    {
        // GroupImportMiddleware associates actors, directors, genres from TMDB
        // data. If group dedup fails, items that trigger the failure are
        // skipped but others may still have groups. This verifies that
        // group association works for at least some items.
        using var doc = await ExecuteGraphQLAsync("""
            {
                mediaItems(
                    first: 5,
                    where: { mediaItemGroups: { some: { group: { slug: { neq: null } } } } }
                ) {
                    nodes {
                        title
                        slug
                        mediaItemGroups {
                            role
                            group {
                                name
                                slug
                            }
                        }
                    }
                }
            }
            """);

        var nodes = doc.RootElement
            .GetProperty("data")
            .GetProperty("mediaItems")
            .GetProperty("nodes");

        await Assert.That(nodes.GetArrayLength()).IsGreaterThan(0);

        // Verify group data is populated (not just the association row)
        var firstItem = nodes[0];
        var groups = firstItem.GetProperty("mediaItemGroups");
        await Assert.That(groups.GetArrayLength()).IsGreaterThan(0);

        var firstGroup = groups[0].GetProperty("group");
        var slug = firstGroup.GetProperty("slug").GetString();
        await Assert.That(slug).IsNotNull();
    }

    [Test]
    public async Task BoxSets_ImportedSuccessfully()
    {
        // Boxsets follow a separate code path in DatabaseImportMiddleware.
        using var doc = await ExecuteGraphQLAsync(
            "{ boxsets(first: 1) { nodes { title slug } } }");

        var nodes = doc.RootElement
            .GetProperty("data")
            .GetProperty("boxsets")
            .GetProperty("nodes");

        await Assert.That(nodes.GetArrayLength()).IsGreaterThan(0);
    }
}
