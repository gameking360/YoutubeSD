using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Upload.Messaging.Persistence;

public static class MySqlDatabase
{
    /// <summary>Connection string por configuração (ConnectionStrings:{name} ou a variável de fallback).</summary>
    public static string ResolveConnectionString(IConfiguration configuration, string name, string fallbackVariable)
    {
        var raw = configuration.GetConnectionString(name)
            ?? configuration[fallbackVariable]
            ?? throw new InvalidOperationException($"Connection string '{name}' não configurada.");

        var builder = new MySqlConnectionStringBuilder(raw);
        if (!raw.Contains("AllowPublicKeyRetrieval", StringComparison.OrdinalIgnoreCase))
        {
            builder.AllowPublicKeyRetrieval = true;
        }

        return builder.ConnectionString;
    }

    public static DbContextOptionsBuilder UseYoutubeMySql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseMySQL(connectionString).UseSnakeCaseNamingConvention();

    /// <summary>
    /// Aplica as migrations versionadas na subida. Cria o database se ele não existir, e o EF trava
    /// a execução para réplicas que sobem juntas.
    /// </summary>
    public static async Task MigrateDatabaseAsync<TContext>(this IServiceProvider services, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        const int maxAttempts = 30;
        var factory = services.GetRequiredService<IDbContextFactory<TContext>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Upload.Migrations");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var context = await factory.CreateDbContextAsync(cancellationToken);
                await context.Database.MigrateAsync(cancellationToken);
                var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
                logger.LogInformation(
                    "Banco {Context} pronto com {Count} migrations aplicadas. instancia={InstanceId}",
                    typeof(TContext).Name, applied.Count(), InstanceInfo.Id);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Falha ao preparar o banco (tentativa {Attempt}/{MaxAttempts}): {Error}",
                    attempt, maxAttempts, ex.GetBaseException().Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
}
