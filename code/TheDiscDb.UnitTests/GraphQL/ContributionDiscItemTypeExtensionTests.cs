using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.GraphQL;

public class ContributionDiscItemTypeExtensionTests
{
    [Test]
    public async Task GetFilename_EmptyDescription_UsesItemName()
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
            Description = string.Empty,
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
}
