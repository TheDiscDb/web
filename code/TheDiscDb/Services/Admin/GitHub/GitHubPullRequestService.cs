using System.Diagnostics;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace TheDiscDb.Services.Admin.GitHub;

public record PrInfo(int Number, bool IsMerged, string AuthorLogin, IReadOnlyList<string> ChangedFiles);

public class GitHubPullRequestService
{
    private readonly IOptions<GitHubOptions> gitHubOptions;
    private readonly ILogger<GitHubPullRequestService> logger;

    public GitHubPullRequestService(
        IOptions<GitHubOptions> gitHubOptions,
        ILogger<GitHubPullRequestService> logger)
    {
        this.gitHubOptions = gitHubOptions;
        this.logger = logger;
    }

    public bool CommitGeneratedFiles(
        string branchName,
        IReadOnlyCollection<string> stagedFiles,
        string commitMessage,
        string repoRoot)
    {
        var options = this.gitHubOptions.Value;
        using var repo = new LibGit2Sharp.Repository(repoRoot);

        var defaultBranch = repo.Branches[options.DefaultBranch]
            ?? throw new InvalidOperationException($"Branch '{options.DefaultBranch}' not found in repository.");

        this.logger.LogInformation("Creating branch: {BranchName}", branchName);

        var existingBranch = repo.Branches[branchName];
        if (existingBranch != null)
        {
            repo.Branches.Remove(existingBranch);
        }

        var newBranch = repo.CreateBranch(branchName, defaultBranch.Tip);
        Commands.Checkout(repo, newBranch);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            foreach (var stagedFile in stagedFiles.Distinct(StringComparer.Ordinal))
            {
                string relativePath = System.IO.Path.IsPathRooted(stagedFile)
                    ? System.IO.Path.GetRelativePath(repoRoot, stagedFile)
                    : stagedFile;

                if (relativePath.StartsWith("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Staged file '{stagedFile}' is outside the repository root.");
                }

                string normalizedPath = relativePath.Replace('\\', '/');
                this.logger.LogInformation("Staging generated file: {File}", normalizedPath);
                Commands.Stage(repo, normalizedPath);
            }

            TreeChanges stagedChanges = repo.Diff.Compare<TreeChanges>(repo.Head.Tip.Tree, DiffTargets.Index);
            if (stagedChanges.Count == 0)
            {
                this.logger.LogWarning("No generated files to commit.");
                return false;
            }

            this.logger.LogInformation("Committing: {Message}", commitMessage);
            var signature = new LibGit2Sharp.Signature(options.CommitAuthorName, options.CommitAuthorEmail, DateTimeOffset.Now);
            repo.Commit(commitMessage, signature, signature);
            this.logger.LogInformation("Commit completed in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
            return true;
        }
        finally
        {
            Commands.Checkout(repo, defaultBranch);
        }
    }

    public void PushBranch(string branchName, string repoRoot)
    {
        var options = this.gitHubOptions.Value;
        using var repo = new LibGit2Sharp.Repository(repoRoot);

        this.logger.LogInformation("Pushing branch to origin...");
        var remote = repo.Network.Remotes["origin"];
        var pushRefSpec = $"refs/heads/{branchName}:refs/heads/{branchName}";
        var pushOptions = new PushOptions
        {
            CredentialsProvider = (url, usernameFromUrl, types) =>
                new UsernamePasswordCredentials
                {
                    Username = "x-access-token",
                    Password = options.Token
                }
        };

        repo.Network.Push(remote, pushRefSpec, pushOptions);
        this.logger.LogInformation("Branch {BranchName} pushed to origin.", branchName);
    }

    public async Task<string> CreatePullRequestAsync(
        string branchName,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        var options = this.gitHubOptions.Value;
        var github = CreateGitHubClient(options);

        this.logger.LogInformation("Creating pull request...");

        var newPr = new NewPullRequest(commitMessage, branchName, options.DefaultBranch)
        {
            Body = $"Automated PR created by TheDiscDb admin import.\n\nBranch: `{branchName}`"
        };

        var pr = await github.PullRequest.Create(options.RepoOwner, options.RepoName, newPr);
        this.logger.LogInformation("Pull request created: {Url}", pr.HtmlUrl);
        return pr.HtmlUrl;
    }

    private static GitHubClient CreateGitHubClient(GitHubOptions options)
    {
        if (string.IsNullOrEmpty(options.Token))
        {
            throw new InvalidOperationException("GitHub token is not configured. Set it via User Secrets: GitHub:Token");
        }

        return new GitHubClient(new Octokit.ProductHeaderValue("TheDiscDb"))
        {
            Credentials = new Octokit.Credentials(options.Token)
        };
    }
}
