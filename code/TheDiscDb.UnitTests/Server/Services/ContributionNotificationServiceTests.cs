namespace TheDiscDb.UnitTests.Server.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sqids;
using TheDiscDb.InputModels;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Email;

public class ContributionNotificationServiceTests
{
    private const string UserEmail = "contributor@example.com";

    // ---- Test infrastructure ---------------------------------------------

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class NoopHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name) => new();
    }

    private sealed class RecordingMailgunClient : MailgunClient
    {
        public RecordingMailgunClient()
            : base(new NoopHttpClientFactory(), new TestOptionsMonitor<MailgunOptions>(new MailgunOptions()))
        {
        }

        public List<MailgunMessage> Sent { get; } = [];

        public override Task<MailgunSendResult> SendAsync(MailgunMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(new MailgunSendResult { Id = "test", Message = "Queued." });
        }
    }

    private sealed class TestDbContextFactory(string dbName) : IDbContextFactory<SqlServerDataContext>
    {
        public SqlServerDataContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<SqlServerDataContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new SqlServerDataContext(options);
        }
    }

    private static ContributionNotificationService CreateService(string dbName, out RecordingMailgunClient mailgun)
    {
        mailgun = new RecordingMailgunClient();
        var options = new TestOptionsMonitor<MailgunOptions>(new MailgunOptions { AdminEmail = "admin@example.com" });
        var idEncoder = new IdEncoder(new SqidsEncoder<int>());
        var factory = new TestDbContextFactory(dbName);
        return new ContributionNotificationService(
            mailgun,
            options,
            idEncoder,
            factory,
            NullLogger<ContributionNotificationService>.Instance);
    }

    private static void SeedMatrixRelease(string dbName)
    {
        var factory = new TestDbContextFactory(dbName);
        using var db = factory.CreateDbContext();
        db.Releases.Add(new Release
        {
            Slug = "4k-uhd",
            Title = "The Matrix",
            Year = 1999,
            MediaItem = new MediaItem
            {
                Slug = "the-matrix",
                Type = "Movie",
                Title = "The Matrix",
                Year = 1999,
            },
        });
        db.SaveChanges();
    }

    private static UserContribution CreateContribution() => new()
    {
        Id = 7,
        Title = "The Matrix",
        Year = "1999",
        TitleSlug = "the-matrix",
        ReleaseTitle = "4K Ultra HD",
        ReleaseSlug = "4k-uhd",
        MediaType = "movie",
    };

    // ---- Tests -----------------------------------------------------------

    [Test]
    public async Task Imported_WhenReleaseResolves_LinksToReleasePage()
    {
        var dbName = Guid.NewGuid().ToString();
        SeedMatrixRelease(dbName);
        var service = CreateService(dbName, out var mailgun);

        await service.NotifyContributionImportedAsync(CreateContribution(), UserEmail);

        var message = mailgun.Sent.Single();
        await Assert.That(message.Html).Contains("https://thediscdb.com/movie/the-matrix/releases/4k-uhd");
        await Assert.That(message.Html).Contains("View this release on TheDiscDb");
    }

    [Test]
    public async Task Imported_WhenOnlyMediaItemResolves_LinksToMediaItemPage()
    {
        var dbName = Guid.NewGuid().ToString();
        SeedMatrixRelease(dbName);
        var service = CreateService(dbName, out var mailgun);

        var contribution = CreateContribution();
        contribution.ReleaseSlug = "does-not-exist";

        await service.NotifyContributionImportedAsync(contribution, UserEmail);

        var message = mailgun.Sent.Single();
        await Assert.That(message.Html).Contains(">View on TheDiscDb<");
        await Assert.That(message.Html).DoesNotContain("/releases/");
        await Assert.That(message.Html).Contains("https://thediscdb.com/movie/the-matrix\"");
    }

    [Test]
    public async Task Imported_WhenNothingResolves_LinksToHome()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName, out var mailgun);

        await service.NotifyContributionImportedAsync(CreateContribution(), UserEmail);

        var message = mailgun.Sent.Single();
        await Assert.That(message.Html).Contains("Visit TheDiscDb");
        await Assert.That(message.Html).DoesNotContain("/releases/");
        await Assert.That(message.Html).Contains("href=\"https://thediscdb.com\"");
    }

    [Test]
    public async Task Imported_WhenNoUserEmail_SendsNothing()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName, out var mailgun);

        await service.NotifyContributionImportedAsync(CreateContribution(), userEmail: null);

        await Assert.That(mailgun.Sent).IsEmpty();
    }
}
