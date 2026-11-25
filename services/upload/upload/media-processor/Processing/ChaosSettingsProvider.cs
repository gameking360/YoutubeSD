using MediaProcessor.Configuration;
using MediaProcessor.Data;
using Microsoft.EntityFrameworkCore;
using Upload.Messaging;

namespace MediaProcessor.Processing;

public sealed record ChaosSnapshot(int LatencyMs, double FailureRate, string Source);

/// <summary>
/// R5 (dependência instável). Os valores padrão vêm de PROCESSOR_LATENCY_MS e
/// PROCESSOR_FAILURE_RATE. A tabela chaos_settings pode sobrepor esses valores ao vivo, em
/// todas as réplicas, sem reiniciar nada.
/// </summary>
public sealed class ChaosSettingsProvider(IDbContextFactory<MediaDbContext> factory, ProcessorOptions options, ILogger<ChaosSettingsProvider> logger)
{
    public async Task<ChaosSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var row = await context.Chaos.AsNoTracking().SingleOrDefaultAsync(chaos => chaos.Id == 1, cancellationToken);
            if (row is not null && (row.LatencyMs is not null || row.FailureRate is not null))
            {
                return new ChaosSnapshot(
                    Math.Max(0, row.LatencyMs ?? options.LatencyMs),
                    Math.Clamp(row.FailureRate ?? options.FailureRate, 0, 1),
                    "chaos_settings");
            }
        }
        catch (Exception ex) when (TransientErrors.IsTransient(ex))
        {
            logger.LogWarning("chaos_settings indisponível; usando as variáveis de ambiente. erro={Error}", ex.GetBaseException().Message);
        }

        return new ChaosSnapshot(options.LatencyMs, options.FailureRate, "ambiente");
    }
}
