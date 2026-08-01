namespace TheDiscDb.UnitTests.GraphQL;

using TheDiscDb.GraphQL.Contribute;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.UnitTests.Data.Changes;

public class ExistingDiscPathValidatorTests
{
    [Test]
    public async Task ValidateAsync_ReleaseDiscSlug_ReturnsParsedPath()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.MediaItem.Externalids.Tmdb = "12345";
        await db.SaveChangesAsync();

        var path = await ExistingDiscPathValidator.ValidateAsync(
            "movie/12345/the-release-slug/disc-one",
            db,
            CancellationToken.None);

        await Assert.That(path).IsEqualTo(
            new ExistingDiscPath("movie", "12345", "the-release-slug", "disc-one"));
    }

    [Test]
    public async Task ValidateAsync_ReleaseDiscIndex_ReturnsParsedPath()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.MediaItem.Externalids.Tmdb = "12345";
        seed.ReleaseDisc.Slug = null;
        await db.SaveChangesAsync();

        var path = await ExistingDiscPathValidator.ValidateAsync(
            "movie/12345/the-release-slug/0",
            db,
            CancellationToken.None);

        await Assert.That(path.DiscSlug).IsEqualTo("0");
    }

    [Test]
    public async Task ValidateAsync_UnknownReleaseDisc_ThrowsInvalidDiscPath()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.MediaItem.Externalids.Tmdb = "12345";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDiscPathException>(() =>
            ExistingDiscPathValidator.ValidateAsync(
                "movie/12345/the-release-slug/missing-disc",
                db,
                CancellationToken.None));
    }
}
