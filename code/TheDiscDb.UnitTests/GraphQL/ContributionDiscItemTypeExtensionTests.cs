using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.GraphQL;

public class ContributionDiscItemTypeExtensionTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("An extended look at the making of the movie.")]
    public async Task GetFilename_UsesItemNameInsteadOfDescription(string? description)
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var database = new SqlServerDataContext(options);
        var contribution = new UserContribution
        {
            UserId = "user",
            Title = "Example Movie",
            Year = "2026",
        };
        var disc = new UserContributionDisc
        {
            UserContribution = contribution,
            Name = "Blu-ray",
            Format = "Blu-ray",
        };
        var item = new UserContributionDiscItem
        {
            Disc = disc,
            Name = "Behind the Scenes",
            Description = description,
            Type = "Extra",
        };

        contribution.Discs.Add(disc);
        disc.Items.Add(item);
        database.UserContributions.Add(contribution);
        await database.SaveChangesAsync();

        var actual = await ContributionDiscItemTypeExtension.GetFilename(
            item,
            database,
            new ClaimsPrincipal(),
            CancellationToken.None);

        await Assert.That(actual).IsEqualTo("Behind the Scenes.mkv");
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task GetFilename_MissingItemName_UsesDiscName(string name)
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var database = new SqlServerDataContext(options);
        var contribution = new UserContribution
        {
            UserId = "user",
            Title = "Example Movie",
            Year = "2026",
        };
        var disc = new UserContributionDisc
        {
            UserContribution = contribution,
            Name = "Bonus Disc",
            Format = "Blu-ray",
        };
        var item = new UserContributionDiscItem
        {
            Disc = disc,
            Name = name,
            Description = "An extended look at the making of the movie.",
            Type = "Extra",
        };

        contribution.Discs.Add(disc);
        disc.Items.Add(item);
        database.UserContributions.Add(contribution);
        await database.SaveChangesAsync();

        var actual = await ContributionDiscItemTypeExtension.GetFilename(
            item,
            database,
            new ClaimsPrincipal(),
            CancellationToken.None);

        await Assert.That(actual).IsEqualTo("Bonus Disc.mkv");
    }
}
