using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
    private readonly HttpClient httpClient;

    public GitHubPullRequestService(
        IOptions<GitHubOptions> gitHubOptions,
        ILogger<GitHubPullRequestService> logger,
        HttpClient httpClient)
    {
        this.gitHubOptions = gitHubOptions;
        this.logger = logger;
        this.httpClient = httpClient;
    }

    public async Task<string> CreatePullRequestAsync(
        string releasePath,
        string titleSlug,
        string releaseSlug,
        string commitMessage,
        string repoRoot,
        CancellationToken cancellationToken = default)
    {
        string titleDirectory = System.IO.Path.GetDirectoryName(releasePath) ?? releasePath;
        string branchName = $"contribution/{titleSlug}/{releaseSlug}";
        return await CreatePullRequestForScopesAsync(branchName, [titleDirectory], commitMessage, repoRoot, cancellationToken);
    }

    public async Task<string> CreatePullRequestForScopesAsync(
        string branchName,
        IReadOnlyCollection<string> scopeDirectories,
        string commitMessage,
        string repoRoot,
        CancellationToken cancellationToken = default)
    {
        var options = this.gitHubOptions.Value;
        var github = CreateGitHubClient(options);

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

        try
        {
            var scopePrefixes = new List<(string RelDir, string PathPrefix)>();
            foreach (var scope in scopeDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string rel = System.IO.Path.GetRelativePath(repoRoot, scope).Replace('\\', '/');
                string prefix = rel.TrimEnd('/') + "/";
                scopePrefixes.Add((rel, prefix));
                this.logger.LogInformation("Staging changes in: {Scope}", rel);
            }

            int stagedCount = 0;
            var status = repo.RetrieveStatus();
            foreach (var entry in status)
            {
                string normalizedPath = entry.FilePath.Replace('\\', '/');
                bool inScope = false;
                foreach (var (rel, prefix) in scopePrefixes)
                {
                    if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || normalizedPath.Equals(rel, StringComparison.OrdinalIgnoreCase))
                    {
                        inScope = true;
                        break;
                    }
                }

                if (inScope)
                {
                    Commands.Stage(repo, entry.FilePath);
                    stagedCount++;
                }
            }

            if (stagedCount == 0)
            {
                this.logger.LogWarning("No changes to commit.");
                return string.Empty;
            }

            this.logger.LogInformation("Committing: {Message}", commitMessage);
            var signature = new LibGit2Sharp.Signature(options.CommitAuthorName, options.CommitAuthorEmail, DateTimeOffset.Now);
            repo.Commit(commitMessage, signature, signature);

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
        }
        finally
        {
            Commands.Checkout(repo, defaultBranch);
        }

        this.logger.LogInformation("Creating pull request...");

        var newPr = new NewPullRequest(commitMessage, branchName, options.DefaultBranch)
        {
            Body = $"Automated PR created by TheDiscDb admin import.\n\nBranch: `{branchName}`"
        };

        var pr = await github.PullRequest.Create(options.RepoOwner, options.RepoName, newPr);
        this.logger.LogInformation("Pull request created: {Url}", pr.HtmlUrl);

        try
        {
            var mergePr = new MergePullRequest { MergeMethod = PullRequestMergeMethod.Squash };
            await github.PullRequest.Merge(options.RepoOwner, options.RepoName, pr.Number, mergePr);
            this.logger.LogInformation("Pull request merged (squash).");
        }
        catch
        {
            bool autoMergeEnabled = await TryEnableAutoMerge(
                pr.NodeId,
                options.Token ?? throw new InvalidOperationException("GitHub token is not configured."),
                cancellationToken);

            if (autoMergeEnabled)
            {
                this.logger.LogInformation("Auto-merge enabled. PR will merge after checks pass.");
            }
            else
            {
                this.logger.LogWarning("Could not auto-merge. PR is open at: {Url}", pr.HtmlUrl);
            }
        }

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

    private async Task<bool> TryEnableAutoMerge(string pullRequestNodeId, string token, CancellationToken cancellationToken)
    {
        try
        {
            var query = new
            {
                query = "mutation($prId: ID!) { enablePullRequestAutoMerge(input: { pullRequestId: $prId, mergeMethod: SQUASH }) { pullRequest { autoMergeRequest { enabledAt } } } }",
                variables = new { prId = pullRequestNodeId }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TheDiscDb", "1.0"));
            request.Content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");

            var response = await this.httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
