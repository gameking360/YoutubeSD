using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace YoutubeSD.Security.Data;

public sealed class MySqlHealthCheck(SecurityDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (MySqlConnector.MySqlException) { return HealthCheckResult.Unhealthy("MySQL indisponível."); }
    }
}
