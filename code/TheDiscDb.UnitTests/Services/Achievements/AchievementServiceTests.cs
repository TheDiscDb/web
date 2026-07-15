namespace TheDiscDb.UnitTests.Services.Achievements;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheDiscDb.Data.Changes.DiscFields;
using TheDiscDb.InputModels;
using TheDiscDb.Services.Achievements;
using TheDiscDb.Web.Data;

public class AchievementServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CancellationToken CT = CancellationToken.None;

    private sealed class TestDbFactory(DbContextOptions<SqlServerDataContext> options)
        : IDbContextFactory<SqlServerDataContext>
    {
        public SqlServerDataContext CreateDbContext() => new(options);
    }

    private static (IDbContextFactory<SqlServerDataContext> Factory, AchievementService Service) CreateService()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var factory = new TestDbFactory(options);
        var builder = new ContributorStatsBuilder(factory);
        var service = new AchievementService(factory, builder, NullLogger<AchievementService>.Instance);
        return (factory, service);
    }

    private static void SeedContributor(
        SqlServerDataContext db, int movieReleases, int seriesReleases,
        int approvedEdits, int discIds, int pending)
    {
        db.Users.Add(new TheDiscDbUser
        {
            Id = "user-1",
            UserName = "octocat",
            NormalizedUserName = "OCTOCAT"
        });

        var contributor = new Contributor { Name = "octocat", UserId = "user-1" };

        void AddRelease(int index, string type)
        {
            var mediaItem = new MediaItem { Slug = $"m{index}-{type}", Title = $"Title {index}", Year = 2000 + index, Type = type };
            var release = new Release
            {
                Slug = $"r{index}",
                Title = $"Release {index}",
                Year = 2000 + index,
                DateAdded = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index),
                MediaItem = mediaItem
            };
            release.Contributors.Add(contributor);
            mediaItem.Releases.Add(release);
            db.Add(mediaItem);
        }

        var idx = 0;
        for (var i = 0; i < movieReleases; i++) AddRelease(idx++, "movie");
        for (var i = 0; i < seriesReleases; i++) AddRelease(idx++, "series");

        for (var i = 0; i < approvedEdits; i++)
        {
            db.EditSuggestions.Add(new EditSuggestion
            {
                UserId = "user-1",
                Status = EditSuggestionStatus.Approved,
                TargetEntityType = "Release",
                Created = DateTimeOffset.UtcNow
            });
        }

        for (var i = 0; i < discIds; i++)
        {
            var details = new DiscFieldsDetails(
                MediaItemSlug: $"m{i}", BoxsetSlug: null, ReleaseSlug: $"r{i}",
                DiscSlug: $"disc{i}", DiscIndex: 0, Name: null, Format: "Blu-ray",
                ContentHash: null, GlobalDiscId: $"ABCDEF00000000000000000000000000000000{i:00}");

            var suggestion = new EditSuggestion
            {
                UserId = "user-1",
                Status = EditSuggestionStatus.Approved,
                Source = EditSuggestionSource.GraphQL,
                TargetEntityType = "Disc",
                Created = DateTimeOffset.UtcNow
            };
            suggestion.Changes.Add(new EditSuggestionChange
            {
                Type = DiscFieldsUpdate.Key,
                Status = EditSuggestionChangeStatus.Applied,
                AppliedAt = DateTimeOffset.UtcNow,
                ProposedJson = JsonSerializer.Serialize(details, JsonOptions)
            });
            db.EditSuggestions.Add(suggestion);
        }

        for (var i = 0; i < pending; i++)
        {
            db.UserContributions.Add(new UserContribution
            {
                UserId = "user-1",
                Status = UserContributionStatus.Pending,
                Created = DateTimeOffset.UtcNow
            });
        }

        db.SaveChanges();
    }

    [Test]
    public async Task EvaluateUserAsync_AwardsExpectedAchievementsAndPoints()
    {
        var (factory, service) = CreateService();
        await using (var db = factory.CreateDbContext())
        {
            SeedContributor(db, movieReleases: 5, seriesReleases: 1, approvedEdits: 1, discIds: 6, pending: 0);
        }

        var result = await service.EvaluateUserAsync("user-1", AchievementAuditActor.Backfill, CT);

        await using var check = factory.CreateDbContext();
        var earned = check.UserAchievements.Select(a => a.AchievementKey).ToHashSet();

        await Assert.That(earned).Contains("first-contribution");
        await Assert.That(earned).Contains("contributor-bronze");
        await Assert.That(earned).Contains("series-contributor");
        await Assert.That(earned).Contains("first-suggested-edit");
        await Assert.That(earned).Contains("first-disc-id");
        await Assert.That(earned).Contains("disc-id-detective-bronze");
        await Assert.That(earned).DoesNotContain("contributor-silver");
        await Assert.That(earned).DoesNotContain("disc-id-detective-gold");

        // 10 + 15 + 20 + 10 + 10 + 15
        await Assert.That(result.TotalPoints).IsEqualTo(80);
        await Assert.That(result.Level).IsEqualTo(LevelCalculator.Archivist);

        // Progress row is written for an un-earned count-based tier.
        // All published releases count (5 movies + 1 series = 6).
        var silver = check.UserAchievementProgress.FirstOrDefault(p => p.AchievementKey == "contributor-silver");
        await Assert.That(silver).IsNotNull();
        await Assert.That(silver!.Current).IsEqualTo(6);
        await Assert.That(silver.Target).IsEqualTo(AchievementThresholds.ContributorSilver);
    }

    [Test]
    public async Task EvaluateUserAsync_IsIdempotent()
    {
        var (factory, service) = CreateService();
        await using (var db = factory.CreateDbContext())
        {
            SeedContributor(db, movieReleases: 5, seriesReleases: 1, approvedEdits: 1, discIds: 6, pending: 0);
        }

        var first = await service.EvaluateUserAsync("user-1", AchievementAuditActor.Backfill, CT);
        var second = await service.EvaluateUserAsync("user-1", AchievementAuditActor.Reconciliation, CT);

        await Assert.That(second.NewlyAwarded).IsEqualTo(0);
        await Assert.That(second.TotalPoints).IsEqualTo(first.TotalPoints);

        await using var check = factory.CreateDbContext();
        var total = check.UserAchievements.Count(a => a.UserId == "user-1");
        var distinct = check.UserAchievements.Where(a => a.UserId == "user-1").Select(a => a.AchievementKey).Distinct().Count();
        await Assert.That(total).IsEqualTo(distinct);
    }

    [Test]
    public async Task EvaluateUserAsync_RevokesCosmeticBadgeWhenNoLongerApplicable()
    {
        var (factory, service) = CreateService();
        await using (var db = factory.CreateDbContext())
        {
            SeedContributor(db, movieReleases: 1, seriesReleases: 0, approvedEdits: 0, discIds: 0, pending: 1);
        }

        await service.EvaluateUserAsync("user-1", AchievementAuditActor.System, CT);
        await using (var check = factory.CreateDbContext())
        {
            var earned = check.UserAchievements.Select(a => a.AchievementKey).ToHashSet();
            await Assert.That(earned).Contains("in-the-works");
        }

        // Clear pending contributions; the cosmetic badge should be revoked on re-evaluation.
        await using (var db = factory.CreateDbContext())
        {
            var pending = db.UserContributions.Where(c => c.UserId == "user-1").ToList();
            db.UserContributions.RemoveRange(pending);
            await db.SaveChangesAsync(CT);
        }

        await service.EvaluateUserAsync("user-1", AchievementAuditActor.System, CT);
        await using (var check = factory.CreateDbContext())
        {
            var earned = check.UserAchievements.Select(a => a.AchievementKey).ToHashSet();
            await Assert.That(earned).DoesNotContain("in-the-works");
            await Assert.That(earned).Contains("first-contribution");
        }
    }

    [Test]
    public async Task GetProfileAsync_ReturnsEarnedAndLevel()
    {
        var (factory, service) = CreateService();
        await using (var db = factory.CreateDbContext())
        {
            SeedContributor(db, movieReleases: 5, seriesReleases: 1, approvedEdits: 1, discIds: 6, pending: 0);
        }

        await service.EvaluateUserAsync("user-1", AchievementAuditActor.Backfill, CT);

        var profile = await service.GetProfileAsync("OCTOCAT", CT);

        await Assert.That(profile.Username).IsEqualTo("octocat");
        await Assert.That(profile.Level).IsEqualTo(LevelCalculator.Archivist);
        await Assert.That(profile.TotalPoints).IsEqualTo(80);
        await Assert.That(profile.Earned.Any(e => e.Definition.Key == "first-contribution")).IsTrue();
        await Assert.That(profile.InProgress.Any(p => p.Definition.Key == "contributor-silver")).IsTrue();
    }

    [Test]
    public async Task GetProfileAsync_UnknownUser_ReturnsEmpty()
    {
        var (_, service) = CreateService();
        var profile = await service.GetProfileAsync("nobody", CT);

        await Assert.That(profile.Earned.Count).IsEqualTo(0);
        await Assert.That(profile.Level).IsEqualTo(LevelCalculator.Newcomer);
    }

    [Test]
    public async Task EvaluateUserAsync_AwardsBreadthAchievementsFromReleaseFacets()
    {
        var (factory, service) = CreateService();
        await using (var db = factory.CreateDbContext())
        {
            SeedBreadth(db);
        }

        await service.EvaluateUserAsync("user-1", AchievementAuditActor.Backfill, CT);

        await using var check = factory.CreateDbContext();
        var earned = check.UserAchievements.Select(a => a.AchievementKey).ToHashSet();

        // 3 distinct formats (DVD, Blu-ray, 4K UHD).
        await Assert.That(earned).Contains("format-collector-bronze");
        await Assert.That(earned).Contains("format-collector-silver");
        // 3 distinct decades (1980s, 1990s, 2000s).
        await Assert.That(earned).Contains("decade-spanner-bronze");
        // Only 4 distinct genres -> genre hopper (needs 5) not earned.
        await Assert.That(earned).DoesNotContain("genre-hopper-bronze");
    }

    [Test]
    public async Task EvaluateUserAsync_CountsBoxsetsSharingADiscWithContributedReleases()
    {
        var (factory, service) = CreateService();
        await using (var db = factory.CreateDbContext())
        {
            SeedBoxsetSharingDisc(db);
        }

        await service.EvaluateUserAsync("user-1", AchievementAuditActor.Backfill, CT);

        await using var check = factory.CreateDbContext();
        var earned = check.UserAchievements.Select(a => a.AchievementKey).ToHashSet();

        // The user contributed a member release whose disc the box set reuses.
        await Assert.That(earned).Contains("boxset-builder-bronze");
    }

    private static void SeedBoxsetSharingDisc(SqlServerDataContext db)
    {
        db.Users.Add(new TheDiscDbUser { Id = "user-1", UserName = "octocat", NormalizedUserName = "OCTOCAT" });
        var contributor = new Contributor { Name = "octocat", UserId = "user-1" };

        // The canonical disc shared between the member release and the box set.
        var sharedDisc = new Disc { Format = "Blu-ray", ContentHash = "shared-hash" };

        // Member release the user actually contributed (not a box set).
        var mediaItem = new MediaItem { Slug = "m0", Title = "Member", Year = 2000, Type = "movie" };
        var memberRelease = new Release
        {
            Slug = "r0",
            Title = "Member Release",
            Year = 2000,
            DateAdded = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            MediaItem = mediaItem
        };
        memberRelease.Discs.Add(new ReleaseDisc { Index = 0, Disc = sharedDisc });
        memberRelease.Contributors.Add(contributor);
        mediaItem.Releases.Add(memberRelease);
        db.Add(mediaItem);

        // Box set that reuses the same disc; its release carries no contributors.
        var boxsetRelease = new Release
        {
            Slug = "set-release",
            Title = "The Box Set",
            Year = 2010,
            DateAdded = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        boxsetRelease.Discs.Add(new ReleaseDisc { Index = 0, Disc = sharedDisc });
        var boxset = new Boxset { Title = "The Box Set", Slug = "the-box-set", Release = boxsetRelease };
        boxsetRelease.Boxset = boxset;
        db.BoxSets.Add(boxset);

        db.SaveChanges();
    }

    private static void SeedBreadth(SqlServerDataContext db)
    {
        db.Users.Add(new TheDiscDbUser { Id = "user-1", UserName = "octocat", NormalizedUserName = "OCTOCAT" });
        var contributor = new Contributor { Name = "octocat", UserId = "user-1" };

        void AddRelease(int index, int year, string genres, string format)
        {
            var mediaItem = new MediaItem { Slug = $"m{index}", Title = $"Title {index}", Year = year, Type = "movie", Genres = genres };
            var disc = new Disc { Format = format, ContentHash = $"hash-{index}" };
            var release = new Release
            {
                Slug = $"r{index}",
                Title = $"Release {index}",
                Year = year,
                DateAdded = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(index),
                MediaItem = mediaItem
            };
            release.Discs.Add(new ReleaseDisc { Index = 0, Disc = disc });
            release.Contributors.Add(contributor);
            mediaItem.Releases.Add(release);
            db.Add(mediaItem);
        }

        AddRelease(0, 1985, "Horror,Thriller", "DVD");
        AddRelease(1, 1995, "Horror,Comedy", "Blu-ray");
        AddRelease(2, 2005, "Sci-Fi", "4K UHD");

        db.SaveChanges();
    }
}
