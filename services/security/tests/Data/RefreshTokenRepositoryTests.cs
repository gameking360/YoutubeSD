using Dapper;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Security.Tests.Infrastructure;
using YoutubeSD.Security.Data;
using YoutubeSD.Security.Security;

namespace Security.Tests.Data;

[Collection("security")]
public sealed class RefreshTokenRepositoryTests(SecurityApiFactory factory)
{
    [Fact]
    public async Task ConcurrentRotationsAcrossConnectionsHaveOnlyOneSuccessAndRevokeOnReplay()
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<RefreshTokenRepository>();
        var original = RefreshTokenGenerator.Hash(RefreshTokenGenerator.Generate());
        var now = DateTime.UtcNow;
        await repository.CreateAsync(Guid.NewGuid().ToString(), factory.UserId, original, now, now.AddDays(7), default);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            repository.RotateAsync(original, RefreshTokenGenerator.Hash(RefreshTokenGenerator.Generate()), now, default)));
        Assert.Single(results, r => r == RotationResult.Rotated);
        Assert.Contains(RotationResult.Reused, results);
        var state = await repository.FindAsync(original, default);
        Assert.NotNull(state!.RevokedAt);
        await using var connection = new MySqlConnection(factory.ConnectionString);
        Assert.Equal(2, await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @Id", new { Id = state.SessionId }));
    }

    [Fact]
    public async Task ExpiredSessionCannotBeRotated()
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<RefreshTokenRepository>();
        var hash = RefreshTokenGenerator.Hash(RefreshTokenGenerator.Generate());
        var now = DateTime.UtcNow;
        await repository.CreateAsync(Guid.NewGuid().ToString(), factory.UserId, hash, now.AddDays(-8), now.AddDays(-1), default);
        Assert.Equal(RotationResult.Invalid, await repository.RotateAsync(hash, RefreshTokenGenerator.Hash("replacement"), now, default));
    }
}
