using MySqlConnector;

namespace YoutubeSD.Security.Data;

public sealed class SecurityDatabase(IConfiguration configuration)
{
    // IDs are strings at the API boundary. Prevent automatic CHAR(36) -> Guid conversion by MySqlConnector.
    private readonly string _connectionString = new MySqlConnectionStringBuilder(
        configuration.GetConnectionString("securityDb")
        ?? throw new InvalidOperationException("ConnectionStrings:securityDb não configurada."))
        { GuidFormat = MySqlGuidFormat.None }.ConnectionString;

    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var settings = new MySqlConnectionStringBuilder(_connectionString);
        var name = settings.Database;
        if (string.IsNullOrWhiteSpace(name) || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new InvalidOperationException("Nome do database inválido.");
        settings.Database = "";
        await using var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
