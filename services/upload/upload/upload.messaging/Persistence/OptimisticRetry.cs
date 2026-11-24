using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Upload.Messaging.Persistence;

/// <summary>
/// Substitui o SELECT ... FOR UPDATE: cada tentativa usa um contexto novo, relê a linha e decide
/// de novo. Se outra réplica salvou antes, o concurrency token gera DbUpdateConcurrencyException.
/// </summary>
public static class OptimisticRetry
{
    public static async Task<T> RunAsync<TContext, T>(
        IDbContextFactory<TContext> factory,
        Func<TContext, Task<T>> work,
        ILogger logger,
        CancellationToken cancellationToken,
        int maxAttempts = 5)
        where TContext : DbContext
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            try
            {
                return await work(context);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                logger.LogInformation(
                    "Conflito de concorrência: outra réplica alterou a mesma linha; relendo e decidindo de novo (tentativa {Attempt}). instancia={InstanceId}",
                    attempt, InstanceInfo.Id);
                await Task.Delay(Random.Shared.Next(10, 50 * attempt), cancellationToken);
            }
        }
    }

    public static Task RunAsync<TContext>(
        IDbContextFactory<TContext> factory,
        Func<TContext, Task> work,
        ILogger logger,
        CancellationToken cancellationToken,
        int maxAttempts = 5)
        where TContext : DbContext =>
        RunAsync(factory, async context =>
        {
            await work(context);
            return true;
        }, logger, cancellationToken, maxAttempts);
}
