using MySqlConnector;

namespace identity.Data;

public sealed class IdentityDatabase
{
    private readonly string _connectionString;

    public IdentityDatabase(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("identityDb")
            ?? configuration["IDENTITY_DB_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("Connection string 'identityDb' não configurada.");
    }

    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            return;
        }
        catch (MySqlException ex) when (ex.Number == 1049)
        {
        }

        var builder = new MySqlConnectionStringBuilder(_connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database) || !database.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            return;
        }

        builder.Database = "";
        await using var server = new MySqlConnection(builder.ConnectionString);
        await server.OpenAsync(cancellationToken);

        await using var command = server.CreateCommand();
        command.CommandText =
            $"CREATE DATABASE IF NOT EXISTS `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
