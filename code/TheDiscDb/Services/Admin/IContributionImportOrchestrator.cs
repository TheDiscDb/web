namespace TheDiscDb.Services.Admin;

public interface IContributionImportOrchestrator
{
    Task<string> RunAsync(
        int contributionId,
        bool overwrite,
        bool import,
        bool createPr,
        Action<string> log,
        CancellationToken cancellationToken = default);
}
