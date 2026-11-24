using System.Data;
using Dapper;

namespace YoutubeSD.Security.Data;

public sealed class RefreshTokenState
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

public enum RotationResult { Rotated, Invalid, Reused }

public sealed class RefreshTokenRepository(SecurityDatabase database)
{
    private const string SelectState = """
        SELECT s.id AS SessionId, s.user_id AS UserId, s.expires_at AS ExpiresAt,
               s.revoked_at AS RevokedAt, t.used_at AS UsedAt
        FROM refresh_tokens t JOIN auth_sessions s ON s.id = t.session_id
        WHERE t.token_hash = @Hash
        """;

    public async Task CreateAsync(string sessionId, string userId, string hash, DateTime now, DateTime expiresAt, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO auth_sessions (id, user_id, created_at, expires_at) VALUES (@SessionId, @UserId, @Now, @ExpiresAt)
            """, new { SessionId = sessionId, UserId = userId, Now = now, ExpiresAt = expiresAt }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO refresh_tokens (token_hash, session_id, created_at) VALUES (@Hash, @SessionId, @Now)
            """, new { Hash = hash, SessionId = sessionId, Now = now }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<RefreshTokenState?> FindAsync(string hash, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<RefreshTokenState>(
            new CommandDefinition(SelectState, new { Hash = hash }, cancellationToken: ct));
    }

    public async Task RevokeAsync(string sessionId, DateTime now, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE auth_sessions SET revoked_at = COALESCE(revoked_at, @Now) WHERE id = @Id",
            new { Id = sessionId, Now = now }, cancellationToken: ct));
    }

    public async Task<RotationResult> RotateAsync(string hash, string replacementHash, DateTime now, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        // Every operation in a token family locks the session first, including concurrent replays.
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var sessionId = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT session_id FROM refresh_tokens WHERE token_hash = @Hash", new { Hash = hash }, tx, cancellationToken: ct));
        if (sessionId is null) return RotationResult.Invalid;
        await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT id FROM auth_sessions WHERE id = @Id FOR UPDATE", new { Id = sessionId }, tx, cancellationToken: ct));
        var state = await connection.QuerySingleAsync<RefreshTokenState>(new CommandDefinition(
            SelectState + " FOR UPDATE", new { Hash = hash }, tx, cancellationToken: ct));
        if (state.RevokedAt is not null || state.ExpiresAt <= now) return RotationResult.Invalid;
        if (state.UsedAt is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition("UPDATE auth_sessions SET revoked_at = @Now WHERE id = @Id",
                new { Now = now, Id = sessionId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return RotationResult.Reused;
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE refresh_tokens SET used_at = @Now, replaced_by_hash = @ReplacementHash WHERE token_hash = @Hash
            """, new { Now = now, ReplacementHash = replacementHash, Hash = hash }, tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO refresh_tokens (token_hash, session_id, created_at) VALUES (@Hash, @SessionId, @Now)
            """, new { Hash = replacementHash, SessionId = sessionId, Now = now }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return RotationResult.Rotated;
    }
}
