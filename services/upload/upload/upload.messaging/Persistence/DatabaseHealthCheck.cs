using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Upload.Messaging.Persistence;

public sealed class DatabaseHealthCheck<TContext>(IDbContextFactory<TContext> factory) : IHealthCheck
    where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("MySQL indisponível.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MySQL indisponível.", ex);
        }
    }
}
