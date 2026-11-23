namespace YoutubeSD.Security.Data;

public sealed class DatabaseInitializer(SecurityDatabase database, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql"), cancellationToken);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await database.EnsureDatabaseAsync(cancellationToken);
                await using var connection = await database.OpenAsync(cancellationToken);
                foreach (var statement in schema.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = statement;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                logger.LogInformation("Schema do Security pronto.");
                return;
            }
            catch (MySqlConnector.MySqlException) when (attempt < 30 && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("MySQL indisponível na inicialização. tentativa={Attempt}", attempt);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}
