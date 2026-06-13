namespace TheDiscDb.Services.Admin;

public class ContributionImportOptions
{
    public string? DataRepositoryPath { get; set; }

    /// <summary>
    /// Persistent shared path where the cached data repository mirror is stored.
    /// In Azure, this would be a mounted Azure Files share. Required for online imports.
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// Maximum time to wait for the cached repository refresh to complete.
    /// Azure Files-backed repos can take longer than the default local checkout.
    /// </summary>
    public int GitPullTimeoutSeconds { get; set; } = 300;
}
