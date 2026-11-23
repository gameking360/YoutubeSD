using Dapper;
using identity.Api;
using identity.Data;
using identity.Security;
using MySqlConnector;

namespace identity.Services;

public sealed class UserService(IdentityDatabase database, IConfiguration configuration)
{
    private const string SelectUser = """
        SELECT u.id AS Id,
               u.name AS Name,
               u.email AS Email,
               u.password_hash AS PasswordHash,
               u.role AS Role,
               u.created_at AS CreatedAt,
               q.quota_limit AS QuotaLimit,
               q.used AS Used,
               q.reserved AS Reserved
        FROM users u
        LEFT JOIN quotas q ON q.user_id = u.id
        """;

    private readonly int _defaultQuotaLimit = Math.Max(1, configuration.GetValue("Identity:DefaultQuotaLimit", 10));

    public async Task<UserResponse> CreateAsync(string name, string email, string password, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var passwordHash = PasswordHasher.Hash(password);

        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO users (id, name, email, password_hash, role, created_at, updated_at)
                VALUES (@Id, @Name, @Email, @PasswordHash, 'USER', @CreatedAt, @UpdatedAt)
                """,
                new { Id = id, Name = name, Email = email, PasswordHash = passwordHash, CreatedAt = now, UpdatedAt = now },
                transaction);

            await connection.ExecuteAsync(
                """
                INSERT INTO quotas (user_id, quota_limit, used, reserved, updated_at)
                VALUES (@UserId, @QuotaLimit, 0, 0, @UpdatedAt)
                """,
                new { UserId = id, QuotaLimit = _defaultQuotaLimit, UpdatedAt = now },
                transaction);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new DuplicateEmailException(email);
        }

        return await GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado após o cadastro.");
    }

    public async Task<UserResponse?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            $"{SelectUser} WHERE u.id = @Id",
            new { Id = id });

        return row is null ? null : ToResponse(row);
    }

    public async Task<AuthenticatedUserResponse?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            $"{SelectUser} WHERE u.email = @Email",
            new { Email = email });

        if (row is null || !PasswordHasher.Verify(password, row.PasswordHash))
        {
            return null;
        }

        return new AuthenticatedUserResponse(row.Id.ToString(), row.Name, row.Email, row.Role);
    }

    public async Task<UserResponse?> UpdateNameAsync(string id, string name, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE users SET name = @Name, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, Name = name, UpdatedAt = DateTime.UtcNow });

        return await GetByIdAsync(id, cancellationToken);
    }

    private UserResponse ToResponse(UserRow row)
    {
        var limit = row.QuotaLimit ?? _defaultQuotaLimit;
        var used = row.Used ?? 0;
        var reserved = row.Reserved ?? 0;

        return new UserResponse(
            row.Id.ToString(),
            row.Name,
            row.Email,
            row.Role,
            DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
            new QuotaResponse(limit, used, reserved, Math.Max(0, limit - used - reserved)));
    }

    private sealed class UserRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Role { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int? QuotaLimit { get; set; }
        public int? Used { get; set; }
        public int? Reserved { get; set; }
    }
}
