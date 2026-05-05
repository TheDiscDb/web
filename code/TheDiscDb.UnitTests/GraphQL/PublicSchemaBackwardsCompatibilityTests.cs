using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheDiscDb.Data.GraphQL;
using TheDiscDb.GraphQL;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.GraphQL;

/// <summary>
/// Verifies that adding the new <c>filename</c> field on <c>Title</c> and
/// the <c>FileNameTemplateInput</c> type is purely additive at the schema
/// level. Existing clients that do not select the new field must continue
/// to receive identical responses.
/// </summary>
public class PublicSchemaBackwardsCompatibilityTests
{
    private const string PreChangeGetDiscDetail = """
        query GetDiscDetail($slug: String, $releaseSlug: String, $discNumber: Int, $discSlug: String, $mediaType: String) {
          mediaItems(where: { and: [ { slug: { eq: $slug } }, { type: { eq: $mediaType } } ] }) {
            nodes {
              id
              title
              year
              slug
              imageUrl
              type
              releases(where: { slug: { eq: $releaseSlug } }) {
                slug
                isbn
                locale
                regionCode
                year
                upc
                title
                imageUrl
                discs(order: { index: ASC }, where: { or: [ { index: { eq: $discNumber } }, { slug: { eq: $discSlug } } ] }) {
                  index
                  name
                  format
                  slug
                  titles(order: { index: ASC }) {
                    index
                    duration
                    displaySize
                    sourceFile
                    size
                    segmentMap
                    item {
                      title
                      season
                      episode
                      type
                      chapters(order: { index: ASC }) {
                        index
                        title
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    private static async Task<IRequestExecutor> BuildExecutorAsync(string dbName)
    {
        var services = new ServiceCollection();

        services.AddDbContextFactory<SqlServerDataContext>(opt =>
            opt.UseInMemoryDatabase(databaseName: dbName));

        services.AddGraphQLServer()
            .ModifyCostOptions(o => o.EnforceCostLimits = false)
            .AddFiltering()
            .AddSorting()
            .AddProjections()
            .RegisterDbContextFactory<SqlServerDataContext>()
            .TryAddTypeInterceptor<TitleItemProjectionTypeInterceptor>()
            .AddTypeExtension<TitleFileNameExtension>()
            .AddQueryType<Query>();

        var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();
    }

    private static void Seed(string dbName)
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        using var db = new SqlServerDataContext(options);

        var media = new MediaItem
        {
            Title = "Inception",
            Year = 2010,
            FullTitle = "Inception (2010)",
            Type = "movie",
            Slug = "inception-2010",
            Externalids = new ExternalIds { Tmdb = "27205", Imdb = "tt1375666" },
        };
        var release = new Release
        {
            Slug = "2010-blu-ray",
            Title = "2010 Blu-ray",
            MediaItem = media,
        };
        var disc = new Disc
        {
            Slug = "disc-1",
            Index = 1,
            Name = "Disc 1",
            Format = "Blu-ray",
            Release = release,
        };
        var item = new DiscItemReference { Title = "Inception", Type = "MainMovie" };
        var track = new Track { Index = 0, Type = "Video", Resolution = "1920x1080" };
        var title = new Title
        {
            Index = 1,
            SourceFile = "title_t01.mkv",
            Disc = disc,
            Item = item,
            Tracks = new List<Track> { track },
        };

        db.MediaItems.Add(media);
        db.Titles.Add(title);
        db.SaveChanges();
    }

    [Test]
    public async Task PreChangeQuery_StillExecutesWithoutErrors()
    {
        var dbName = Guid.NewGuid().ToString();
        Seed(dbName);

        var executor = await BuildExecutorAsync(dbName);
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder.New()
                .SetDocument(PreChangeGetDiscDetail)
                .SetVariableValues(new Dictionary<string, object?>
                {
                    ["slug"] = "inception-2010",
                    ["releaseSlug"] = "2010-blu-ray",
                    ["discNumber"] = 1,
                    ["discSlug"] = "disc-1",
                    ["mediaType"] = "movie",
                })
                .Build());

        var json = result.ToJson();
        await Assert.That(json).Contains("Inception");
        await Assert.That(json).Contains("title_t01.mkv");
        await Assert.That(json).DoesNotContain("\"errors\"");
    }

    [Test]
    public async Task TitleType_RetainsAllPriorFields()
    {
        var executor = await BuildExecutorAsync(Guid.NewGuid().ToString());
        var schema = executor.Schema;
        var titleType = schema.GetType<ObjectType>(nameof(Title));

        // Every field that existed on Title before the change must still exist.
        string[] priorFieldNames =
        [
            "index", "disc", "id", "comment", "sourceFile", "segmentMap",
            "duration", "size", "displaySize", "item", "discItemReferenceId",
            "tracks", "description", "itemType", "season", "episode", "hasItem",
        ];

        foreach (var name in priorFieldNames)
        {
            await Assert.That(titleType.Fields.ContainsField(name))
                .IsTrue();
        }
    }

    [Test]
    public async Task TitleType_HasNewFilenameField()
    {
        var executor = await BuildExecutorAsync(Guid.NewGuid().ToString());
        var schema = executor.Schema;
        var titleType = schema.GetType<ObjectType>(nameof(Title));

        await Assert.That(titleType.Fields.ContainsField("filename")).IsTrue();
    }

    [Test]
    public async Task QueryWithoutFilename_ReturnsNoFilenameKey()
    {
        var dbName = Guid.NewGuid().ToString();
        Seed(dbName);

        var executor = await BuildExecutorAsync(dbName);
        var result = await executor.ExecuteAsync("""
            query {
              mediaItems(where: { slug: { eq: "inception-2010" } }) {
                nodes {
                  releases {
                    discs {
                      titles {
                        sourceFile
                        description
                        itemType
                      }
                    }
                  }
                }
              }
            }
            """);

        var json = result.ToJson();
        await Assert.That(json).DoesNotContain("\"filename\"");
        await Assert.That(json).DoesNotContain("\"errors\"");
    }

    [Test]
    public async Task FilenameWithNoTemplatesArgument_UsesDefaults()
    {
        var dbName = Guid.NewGuid().ToString();
        Seed(dbName);

        var executor = await BuildExecutorAsync(dbName);
        var result = await executor.ExecuteAsync("""
            query {
              mediaItems(where: { slug: { eq: "inception-2010" } }) {
                nodes {
                  releases {
                    discs {
                      titles {
                        filename
                      }
                    }
                  }
                }
              }
            }
            """);

        var json = result.ToJson();
        await Assert.That(json).Contains("Inception (2010) [1080p].mkv");
        await Assert.That(json).DoesNotContain("\"errors\"");
    }
}
