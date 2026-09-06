namespace TheDiscDb.UnitTests.Server.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sqids;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;
using TheDiscDb.Web.Email;

public class ContributionNotificationServiceTests
{
    [Test]
    public async Task NotifyContributionImportedAsync_ReleaseSlugPresent_LinksToRelease()
    {
        var (service, mailgun) = CreateService();
        var contribution = CreateContribution();

        await service.NotifyContributionImportedAsync(contribution, "user@example.com");

        await Assert.That(mailgun.Sent.Count).IsEqualTo(1);
        await Assert.That(mailgun.Sent[0].Html).Contains(
            "href=\"https://thediscdb.com/movie/project-hail-mary-2026/releases/4k-uhd\"");
    }

    [Test]
    public async Task NotifyContributionImportedAsync_ReleaseSlugMissing_LinksToTitle()
    {
        var (service, mailgun) = CreateService();
        var contribution = CreateContribution();
        contribution.ReleaseSlug = null;

        await service.NotifyContributionImportedAsync(contribution, "user@example.com");

        await Assert.That(mailgun.Sent.Count).IsEqualTo(1);
        await Assert.That(mailgun.Sent[0].Html).Contains(
            "href=\"https://thediscdb.com/movie/project-hail-mary-2026\"");
    }

    [Test]
    public async Task NotifyContributionImportedAsync_TitleSlugMissing_LinksToHomepage()
    {
        var (service, mailgun) = CreateService();
        var contribution = CreateContribution();
        contribution.TitleSlug = string.Empty;

        await service.NotifyContributionImportedAsync(contribution, "user@example.com");

        await Assert.That(mailgun.Sent.Count).IsEqualTo(1);
        await Assert.That(mailgun.Sent[0].Html).Contains(
            "href=\"https://thediscdb.com\"");
        await Assert.That(mailgun.Sent[0].Html).Contains(">Visit TheDiscDb</a>");
    }

    private static (ContributionNotificationService Service, RecordingMailgunClient Mailgun) CreateService()
    {
        var mailgun = new RecordingMailgunClient();
        var options = new TestOptionsMonitor<MailgunOptions>(new MailgunOptions
        {
            AdminEmail = "admin@example.com",
        });
        var service = new ContributionNotificationService(
            mailgun,
            options,
            new IdEncoder(new SqidsEncoder<int>()),
            NullLogger<ContributionNotificationService>.Instance);

        return (service, mailgun);
    }

    private static UserContribution CreateContribution() => new()
    {
        Id = 42,
        Title = "Project Hail Mary",
        Year = "2026",
        ReleaseTitle = "4K UHD",
        MediaType = "Movie",
        TitleSlug = "project-hail-mary-2026",
        ReleaseSlug = "4k-uhd",
    };

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => this.CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class RecordingMailgunClient()
        : MailgunClient(
            new NoopHttpClientFactory(),
            new TestOptionsMonitor<MailgunOptions>(new MailgunOptions()))
    {
        public List<MailgunMessage> Sent { get; } = [];

        public override Task<MailgunSendResult> SendAsync(
            MailgunMessage message,
            CancellationToken cancellationToken = default)
        {
            this.Sent.Add(message);
            return Task.FromResult(new MailgunSendResult { Id = "test", Message = "Queued." });
        }
    }
}
