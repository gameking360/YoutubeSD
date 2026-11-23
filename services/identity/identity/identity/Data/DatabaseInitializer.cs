using identity.Observability;

namespace identity.Data;

public sealed class DatabaseInitializer(IdentityDatabase database, ILogger<DatabaseInitializer> logger)
{
    private const int MaxAttempts = 30;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql");
        var schema = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        var statements = schema.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await database.EnsureDatabaseAsync(cancellationToken);
                await using var connection = await database.OpenAsync(cancellationToken);
                foreach (var statement in statements)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = statement;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                logger.LogInformation("Schema do identity pronto. instancia={InstanceId}", InstanceInfo.Id);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex, "Falha ao preparar o banco (tentativa {Attempt}/{MaxAttempts}).", attempt, MaxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}
