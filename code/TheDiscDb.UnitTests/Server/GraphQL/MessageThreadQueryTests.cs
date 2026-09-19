namespace TheDiscDb.UnitTests.Server.GraphQL;

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb.GraphQL.Contribute;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

public class MessageThreadQueryTests
{
    [Test]
    public async Task GetMessageThreads_IncludesContributionBoxsetAndEditSuggestion()
    {
        await using var db = CreateDb();
        db.UserContributions.Add(new UserContribution
        {
            Id = 10,
            UserId = "user-1",
            Title = "Movie",
            ReleaseTitle = "4K release",
            Year = "2025",
            MediaType = "movie",
            Asin = "B000000000",
            Upc = "123456789012",
            Status = UserContributionStatus.Pending,
            Created = DateTimeOffset.UtcNow,
        });
        db.UserContributionBoxsets.Add(new UserContributionBoxset
        {
            Id = 20,
            UserId = "user-1",
            Title = "Collection",
            Created = DateTimeOffset.UtcNow,
        });
        db.EditSuggestions.Add(new EditSuggestion
        {
            Id = 30,
            UserId = "user-1",
            Created = DateTimeOffset.UtcNow,
            Status = EditSuggestionStatus.ChangesRequested,
            Summary = "Correct release title",
            TargetEntityType = "Release",
            TargetEntityKey = "movie/release",
        });
        db.UserMessages.AddRange(
            Message(contributionId: 10, createdOffset: -3),
            Message(boxsetId: 20, createdOffset: -2),
            Message(editSuggestionId: 30, createdOffset: -1));
        await db.SaveChangesAsync();

        var idEncoder = new IdEncoder(new SqidsEncoder<int>());
        var query = new ContributionQuery(idEncoder);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
        ], "test"));

        var threads = await query.GetMessageThreads(db, principal);

        await Assert.That(threads).Count().IsEqualTo(3);
        var suggestion = threads.Single(thread => thread.Kind == MessageThreadKind.EditSuggestion);
        await Assert.That(suggestion.Route).IsEqualTo($"/changes/my/{idEncoder.Encode(30)}");
        await Assert.That(suggestion.Title).IsEqualTo("Correct release title");
        await Assert.That(suggestion.UnreadCount).IsEqualTo(1);
        await Assert.That(threads.Any(thread => thread.Kind == MessageThreadKind.Contribution)).IsTrue();
        await Assert.That(threads.Any(thread => thread.Kind == MessageThreadKind.Boxset)).IsTrue();
    }

    [Test]
    public async Task GetMessageThreads_AdminParticipantUsesAdminSuggestionRoute()
    {
        await using var db = CreateDb();
        db.EditSuggestions.Add(new EditSuggestion
        {
            Id = 30,
            UserId = "user-1",
            Created = DateTimeOffset.UtcNow,
            Status = EditSuggestionStatus.ChangesRequested,
            Summary = "Correct release title",
            TargetEntityType = "Release",
            TargetEntityKey = "movie/release",
        });
        db.UserMessages.Add(new UserMessage
        {
            EditSuggestionId = 30,
            FromUserId = "user-1",
            ToUserId = "admin-1",
            Message = "Clarification",
            CreatedAt = DateTimeOffset.UtcNow,
            Type = UserMessageType.UserMessage,
        });
        await db.SaveChangesAsync();

        var query = new ContributionQuery(new IdEncoder(new SqidsEncoder<int>()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "admin-1"),
            new Claim(ClaimTypes.Role, DefaultRoles.Administrator),
        ], "test"));

        var thread = (await query.GetMessageThreads(db, principal)).Single();

        await Assert.That(thread.Kind).IsEqualTo(MessageThreadKind.EditSuggestion);
        await Assert.That(thread.Route).IsEqualTo("/admin/changes/30");
    }

    private static UserMessage Message(
        int? contributionId = null,
        int? boxsetId = null,
        int? editSuggestionId = null,
        int createdOffset = 0) =>
        new()
        {
            ContributionId = contributionId,
            BoxsetId = boxsetId,
            EditSuggestionId = editSuggestionId,
            FromUserId = "admin-1",
            ToUserId = "user-1",
            Message = "Please review",
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(createdOffset),
            Type = UserMessageType.AdminMessage,
        };

    private static SqlServerDataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SqlServerDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlServerDataContext(options);
    }
}
