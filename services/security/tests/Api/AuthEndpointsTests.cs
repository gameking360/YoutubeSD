using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.IdentityModel.JsonWebTokens;
using MySqlConnector;
using Security.Tests.Infrastructure;
using YoutubeSD.Security.Api;
using YoutubeSD.Security.Security;

namespace Security.Tests.Api;

[Collection("security")]
public sealed class AuthEndpointsTests
{
    private readonly SecurityApiFactory _factory;
    public AuthEndpointsTests(SecurityApiFactory factory) { _factory = factory; factory.Reset(); }

    private async Task<TokenResponse> Login(HttpClient? client = null)
    {
        var response = await (client ?? _factory.Client).PostAsJsonAsync("/api/auth/login", new { email = " ANA@teste.local ", password = "senha123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    [Fact]
    public async Task LoginReturnsRequiredClaimsAndStoresOnlyHash()
    {
        var tokens = await Login();
        var jwt = new JsonWebToken(tokens.AccessToken);
        Assert.Equal(_factory.UserId, jwt.Subject);
        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal(new[] { "USER" }, jwt.GetPayloadValue<string[]>("roles"));
        Assert.Equal("youtubesd-security", jwt.Issuer);
        Assert.Contains("youtubesd-api", jwt.Audiences);
        Assert.Equal(900, tokens.ExpiresIn);
        Assert.InRange((jwt.ValidTo - jwt.IssuedAt).TotalSeconds, 899, 901);
        Assert.False(string.IsNullOrWhiteSpace(jwt.Kid));
        await using var connection = new MySqlConnection(_factory.ConnectionString);
        var hash = await connection.QuerySingleAsync<string>("SELECT token_hash FROM refresh_tokens WHERE token_hash = @Hash",
            new { Hash = RefreshTokenGenerator.Hash(tokens.RefreshToken) });
        Assert.NotEqual(tokens.RefreshToken, hash);
    }

    [Fact]
    public async Task InvalidCredentialsAndMalformedInputsAreRejected()
    {
        using var client = _factory.Client;
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "ana@teste.local", password = "wrong" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "unknown@teste.local", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var wrongBody = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(wrongBody.GetProperty("title").GetString(), unknownBody.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/refresh", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "unknown" })).StatusCode);
    }

    [Fact]
    public async Task RefreshUpdatesRolesAndReplayRevokesEntireSession()
    {
        var original = await Login();
        _factory.Role = "ADMIN";
        using var client = _factory.Client;
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { original.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var next = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(original.RefreshToken, next.RefreshToken);
        Assert.Equal(new[] { "ADMIN" }, new JsonWebToken(next.AccessToken).GetPayloadValue<string[]>("roles"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { original.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { next.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task IdentityFailureAndTimeoutReturn503WithoutConsumingRefresh()
    {
        var original = await Login();
        using var client = _factory.Client;
        _factory.IdentityFailure = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/refresh", new { original.RefreshToken })).StatusCode);
        _factory.IdentityFailure = null;
        _factory.IdentityTimeout = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/login", new { email = "ana@teste.local", password = "senha123" })).StatusCode);
        _factory.IdentityTimeout = false;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/refresh", new { original.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task ExpiredRefreshIsRejected()
    {
        var original = await Login();
        _factory.Clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.Client.PostAsJsonAsync("/api/auth/refresh", new { original.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task DeletedUserCannotRefresh()
    {
        var original = await Login();
        _factory.IdentityFailure = HttpStatusCode.NotFound;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.Client.PostAsJsonAsync("/api/auth/refresh", new { original.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task AnotherApplicationInstanceUsesSameKeyAndPersistentSession()
    {
        var original = await Login();
        using var second = _factory.WithWebHostBuilder(_ => { });
        using var client = second.CreateClient();
        var firstJwks = await _factory.Client.GetStringAsync("/api/auth/.well-known/jwks.json");
        Assert.Equal(firstJwks, await client.GetStringAsync("/api/auth/.well-known/jwks.json"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/refresh", new { original.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task JwksContainsOnlyPublicRsaParametersAndHealthWorksInProduction()
    {
        using var client = _factory.Client;
        var document = JsonDocument.Parse(await client.GetStringAsync("/api/auth/.well-known/jwks.json"));
        var key = document.RootElement.GetProperty("keys")[0];
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.False(key.TryGetProperty("d", out _));
        Assert.False(key.TryGetProperty("p", out _));
        Assert.False(key.TryGetProperty("q", out _));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
    }
}
