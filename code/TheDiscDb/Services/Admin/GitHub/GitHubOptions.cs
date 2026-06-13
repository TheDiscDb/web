namespace TheDiscDb.Services.Admin.GitHub;

public class GitHubOptions
{
    public string? Token { get; set; }
    public string RepoOwner { get; set; } = "TheDiscDb";
    public string RepoName { get; set; } = "data";
    public string DefaultBranch { get; set; } = "main";
    public string CommitAuthorName { get; set; } = string.Empty;
    public string CommitAuthorEmail { get; set; } = string.Empty;
}
