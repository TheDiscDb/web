using System.Diagnostics;
using Microsoft.Extensions.Options;
using TheDiscDb.Services.Admin;

namespace TheDiscDb.Services.Admin.Workspace;

/// <summary>
/// Refreshes a cached data repository mirror by running <c>git pull</c>.
/// Assumes the repository already exists at the given path.
/// Runs before each import to ensure branches and references are fresh.
/// </summary>
public class CachedDataRepositoryInitializer
{
    private readonly ILogger<CachedDataRepositoryInitializer> logger;
    private readonly int gitPullTimeoutSeconds;

    public CachedDataRepositoryInitializer(
        ILogger<CachedDataRepositoryInitializer> logger,
        IOptions<ContributionImportOptions> options)
    {
        this.logger = logger;
        this.gitPullTimeoutSeconds = options.Value.GitPullTimeoutSeconds > 0
            ? options.Value.GitPullTimeoutSeconds
            : 300;
    }

    /// <summary>
    /// Refresh the cached repository by running <c>git pull</c>.
    /// </summary>
    /// <param name="repoRootPath">Path to the existing cached repository root directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the repository does not exist or git pull fails.
    /// </exception>
    public async Task RefreshAsync(string repoRootPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repoRootPath))
        {
            throw new InvalidOperationException(
                $"Cached repository does not exist at '{repoRootPath}'. " +
                "Ensure the repository has been cloned to this location.");
        }

        this.logger.LogInformation("Refreshing cached repository at {RepoPath}", repoRootPath);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "pull",
                WorkingDirectory = repoRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start git process.");

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(this.gitPullTimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw new InvalidOperationException($"git pull timed out after {this.gitPullTimeoutSeconds} seconds.");
            }

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                this.logger.LogError(
                    "git pull failed with exit code {ExitCode}. Error: {Error}",
                    process.ExitCode,
                    error);
                throw new InvalidOperationException(
                    $"git pull failed with exit code {process.ExitCode}: {error}");
            }

            this.logger.LogInformation("Repository refreshed successfully. Output: {Output}", output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            this.logger.LogError(ex, "Error refreshing cached repository");
            throw new InvalidOperationException(
                $"Error refreshing cached repository at '{repoRootPath}': {ex.Message}",
                ex);
        }
    }
}
