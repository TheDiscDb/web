using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sqids;
using TheDiscDb.Services;
using TheDiscDb.Services.Admin.GitHub;
using TheDiscDb.Services.Admin.Workspace;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services.Admin;

public class ContributionImportOrchestrator : IContributionImportOrchestrator
{
    private readonly ContributionGeneratorService generatorService;
    private readonly ContributionImportPipelineRunner pipelineRunner;
    private readonly GitHubPullRequestService prService;
    private readonly IDataRepositoryWorkspaceFactory workspaceFactory;
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly IContributionHistoryService historyService;
    private readonly IContributionNotificationService notificationService;
    private readonly ILogger<ContributionImportOrchestrator> logger;
    private readonly UserManager<TheDiscDbUser> userManager;
    private readonly SqidsEncoder<int> idEncoder;

    public ContributionImportOrchestrator(
        ContributionGeneratorService generatorService,
        ContributionImportPipelineRunner pipelineRunner,
        GitHubPullRequestService prService,
        IDataRepositoryWorkspaceFactory workspaceFactory,
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        IContributionHistoryService historyService,
        IContributionNotificationService notificationService,
        ILogger<ContributionImportOrchestrator> logger,
        UserManager<TheDiscDbUser> userManager,
        SqidsEncoder<int> idEncoder)
    {
        this.generatorService = generatorService;
        this.pipelineRunner = pipelineRunner;
        this.prService = prService;
        this.workspaceFactory = workspaceFactory;
        this.dbContextFactory = dbContextFactory;
        this.historyService = historyService;
        this.notificationService = notificationService;
        this.logger = logger;
        this.userManager = userManager;
        this.idEncoder = idEncoder;
    }

    public async Task<string> RunAsync(
        int contributionId,
        bool overwrite,
        bool import,
        bool createPr,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        string prUrl = string.Empty;

        await using var workspace = await this.workspaceFactory.CreateAsync(cancellationToken);
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
        var contribution = await dbContext.UserContributions
            .FirstOrDefaultAsync(c => c.Id == contributionId, cancellationToken)
            ?? throw new InvalidOperationException($"Contribution {contributionId} not found.");

        if (contribution.Status is UserContributionStatus.Pending or UserContributionStatus.ReadyForReview)
        {
            await this.UpdateContributionStatusAsync(contribution, UserContributionStatus.Approved, dbContext, log, cancellationToken);
        }

        log("Step 1: Generating contribution artifacts...");
        string releaseDirectory = await this.generatorService.GenerateAsync(
            contributionId,
            overwrite,
            workspace,
            log,
            cancellationToken);
        log($"Generation complete. Release directory: {releaseDirectory}");

        if (import)
        {
            log("Step 2: Importing artifacts into database...");
            await this.pipelineRunner.RunAsync(releaseDirectory, log, cancellationToken);

            if (contribution.Status != UserContributionStatus.Imported)
            {
                await this.UpdateContributionStatusAsync(contribution, UserContributionStatus.Imported, dbContext, log, cancellationToken);
                await this.NotifyContributionImportedAsync(contribution, cancellationToken);
            }
        }

        if (createPr)
        {
            log("Step 3: Creating GitHub pull request...");

            string contributorName = "unknown";
            var user = await this.userManager.FindByIdAsync(contribution.UserId);
            if (user != null)
            {
                await using var contributorContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken);
                var contributor = await contributorContext.Contributors
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.UserId == user.Id || c.Name == user.UserName, cancellationToken);
                contributorName = contributor?.Name ?? user.UserName ?? "unknown";
            }

            string prCommitMessage = $"{contribution.Title} {contribution.ReleaseTitle} contributed by {contributorName}";
            prUrl = await this.prService.CreatePullRequestAsync(
                releaseDirectory,
                contribution.TitleSlug ?? contribution.Title?.ToLowerInvariant().Replace(" ", "-") ?? "unknown",
                contribution.ReleaseSlug ?? "release",
                prCommitMessage,
                workspace.RepoRootPath,
                cancellationToken);

            log($"Pull request created: {prUrl}");
        }

        log("All steps complete.");
        return prUrl;
    }

    private async Task UpdateContributionStatusAsync(
        UserContribution contribution,
        UserContributionStatus newStatus,
        SqlServerDataContext dbContext,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var oldStatus = contribution.Status;
        contribution.Status = newStatus;
        await dbContext.SaveChangesAsync(cancellationToken);
        await this.historyService.RecordStatusChangedAsync(contribution.Id, contribution.UserId, oldStatus, newStatus, cancellationToken);
        log($"Contribution status updated: {newStatus}");
    }

    private async Task NotifyContributionImportedAsync(UserContribution contribution, CancellationToken cancellationToken)
    {
        var user = await this.userManager.FindByIdAsync(contribution.UserId);

        try
        {
            await this.notificationService.NotifyContributionImportedAsync(contribution, user?.Email, cancellationToken);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to send imported notification for contribution {Id}", contribution.Id);
        }
    }
}
