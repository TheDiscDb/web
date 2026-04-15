using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TheDiscDb.DatabaseMigration;

public class SeedingHealthCheck : IHealthCheck
{
    private volatile bool _isReady;

    public void MarkReady() => _isReady = true;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isReady
            ? HealthCheckResult.Healthy("Initial seeding complete.")
            : HealthCheckResult.Unhealthy("Seeding in progress."));
    }
}
