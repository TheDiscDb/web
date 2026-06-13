namespace TheDiscDb.Services.Admin.Workspace;

public interface IDataRepositoryWorkspace : IAsyncDisposable
{
    string DataRepositoryPath { get; }
    string RepoRootPath { get; }
}

public interface IDataRepositoryWorkspaceFactory
{
    Task<IDataRepositoryWorkspace> CreateAsync(CancellationToken cancellationToken = default);
}
