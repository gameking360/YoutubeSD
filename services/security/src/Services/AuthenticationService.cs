using Microsoft.Extensions.Options;
using YoutubeSD.Security.Api;
using YoutubeSD.Security.Configuration;
using YoutubeSD.Security.Data;
using YoutubeSD.Security.Integration;
using YoutubeSD.Security.Security;

namespace YoutubeSD.Security.Services;

public sealed class AuthenticationService(IdentityClient identity, RefreshTokenRepository tokens,
    AccessTokenIssuer issuer, IOptions<SecurityOptions> options, TimeProvider clock, ILogger<AuthenticationService> logger)
{
    public async Task<TokenResponse?> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await identity.AuthenticateAsync(email, password, ct);
        if (user is null) { logger.LogInformation("Login recusado."); return null; }
        var sessionId = Guid.NewGuid().ToString();
        var refresh = RefreshTokenGenerator.Generate();
        var access = issuer.Issue(user.UserId, user.Role);
        var now = clock.GetUtcNow().UtcDateTime;
        await tokens.CreateAsync(sessionId, user.UserId, RefreshTokenGenerator.Hash(refresh), now,
            now.AddDays(options.Value.RefreshTokenDays), ct);
        logger.LogInformation("Login realizado. userId={UserId} sessionId={SessionId}", user.UserId, sessionId);
        return new TokenResponse(access, refresh, issuer.ExpiresIn);
    }

    public async Task<TokenResponse?> RefreshAsync(string refresh, CancellationToken ct)
    {
        var hash = RefreshTokenGenerator.Hash(refresh);
        var state = await tokens.FindAsync(hash, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (state is null || state.RevokedAt is not null || state.ExpiresAt <= now) return null;
        if (state.UsedAt is not null)
        {
            await tokens.RevokeAsync(state.SessionId, now, ct);
            logger.LogWarning("Reutilização de refresh detectada; sessão revogada. sessionId={SessionId}", state.SessionId);
            return null;
        }
        var user = await identity.GetUserAsync(state.UserId, ct);
        if (user is null)
        {
            await tokens.RevokeAsync(state.SessionId, now, ct);
            return null;
        }
        if (!string.Equals(user.UserId, state.UserId, StringComparison.OrdinalIgnoreCase))
            throw new IdentityUnavailableException();
        var next = RefreshTokenGenerator.Generate();
        var access = issuer.Issue(user.UserId, user.Role);
        var result = await tokens.RotateAsync(hash, RefreshTokenGenerator.Hash(next), clock.GetUtcNow().UtcDateTime, ct);
        logger.LogInformation("Renovação de sessão. sessionId={SessionId} resultado={Result}", state.SessionId, result);
        return result == RotationResult.Rotated ? new TokenResponse(access, next, issuer.ExpiresIn) : null;
    }
}
