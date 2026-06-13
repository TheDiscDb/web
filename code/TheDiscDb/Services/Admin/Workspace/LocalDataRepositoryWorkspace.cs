namespace TheDiscDb.Services.Admin.Workspace;

public class LocalDataRepositoryWorkspace : IDataRepositoryWorkspace
{
    private readonly string workDirectoryPath;

    public string DataRepositoryPath { get; }
    public string RepoRootPath { get; }

    /// <summary>
    /// Creates a workspace backed by a cached repository.
    /// </summary>
    /// <param name="dataRepositoryPath">Path to the data directory inside the cached repository.</param>
    /// <param name="repoRootPath">Path to the cached repository root.</param>
    /// <param name="workDirectoryPath">Path to a temporary work directory for this import (will be deleted on dispose).</param>
    public LocalDataRepositoryWorkspace(string dataRepositoryPath, string repoRootPath, string workDirectoryPath)
    {
        DataRepositoryPath = System.IO.Path.GetFullPath(dataRepositoryPath);
        RepoRootPath = System.IO.Path.GetFullPath(repoRootPath);
        this.workDirectoryPath = workDirectoryPath;
    }

    public async ValueTask DisposeAsync()
    {
        if (Directory.Exists(this.workDirectoryPath))
        {
            try
            {
                Directory.Delete(this.workDirectoryPath, recursive: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to delete work directory '{this.workDirectoryPath}': {ex.Message}");
            }
        }

        await ValueTask.CompletedTask;
    }
}

public class LocalDataRepositoryWorkspaceFactory : IDataRepositoryWorkspaceFactory
{
    private readonly IConfiguration configuration;
    private readonly ILogger<LocalDataRepositoryWorkspaceFactory> logger;

    public LocalDataRepositoryWorkspaceFactory(
        IConfiguration configuration,
        ILogger<LocalDataRepositoryWorkspaceFactory> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
    }

    public Task<IDataRepositoryWorkspace> CreateAsync(CancellationToken cancellationToken = default)
    {
        var workspacePath = this.configuration.GetValue<string>("ContributionImport:WorkspacePath")
            ?? throw new InvalidOperationException(
                "ContributionImport:WorkspacePath is not configured. " +
                "Set this to a persistent shared directory (e.g., mounted Azure Files) where the cached " +
                "data repository will be stored and workspace directories created for each import.");

        var cachedRepoPath = this.configuration.GetValue<string>("ContributionImport:DataRepositoryPath")
            ?? throw new InvalidOperationException(
                "ContributionImport:DataRepositoryPath is not configured. " +
                "Set this to the cached data repository checkout that will be refreshed with git pull.");

        workspacePath = System.IO.Path.GetFullPath(workspacePath);
        cachedRepoPath = System.IO.Path.GetFullPath(cachedRepoPath);
        cachedRepoPath = System.IO.Path.TrimEndingDirectorySeparator(cachedRepoPath);

        this.logger.LogInformation(
            "Initializing workspace with cache at {CachePath}",
            cachedRepoPath);

        string repoRootPath = System.IO.Path.GetDirectoryName(cachedRepoPath)
            ?? throw new InvalidOperationException(
                $"Could not determine the repository root from '{cachedRepoPath}'.");

        // Create a unique temporary directory for this import's work
        string workDirectoryPath = System.IO.Path.Combine(
            workspacePath,
            $"work-{Guid.NewGuid():N}");

        Directory.CreateDirectory(workDirectoryPath);

        this.logger.LogInformation(
            "Created work directory at {WorkPath}",
            workDirectoryPath);

        return Task.FromResult<IDataRepositoryWorkspace>(
            new LocalDataRepositoryWorkspace(cachedRepoPath, repoRootPath, workDirectoryPath));
    }
}
