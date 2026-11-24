using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using YoutubeSD.Security.Configuration;
using YoutubeSD.Security.Security;

namespace Security.Tests.Security;

public sealed class AccessTokenTests
{
    private static SecurityOptions Options(RSA rsa) => new()
    {
        SigningKeyBase64 = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()),
        Issuer = "issuer", Audience = "audience"
    };

    [Fact]
    public async Task PublicJwksValidatesSignatureAndRejectsWrongIssuerAudienceAndKey()
    {
        using var rsa = RSA.Create(2048);
        var options = Microsoft.Extensions.Options.Options.Create(Options(rsa));
        using var keys = new SigningKeyProvider(options);
        var issuer = new AccessTokenIssuer(keys, options, TimeProvider.System);
        var token = issuer.Issue(Guid.NewGuid().ToString(), "USER");
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = "issuer", ValidAudience = "audience", ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = new JsonWebKeySet(JsonSerializer.Serialize(keys.PublicJwks())).GetSigningKeys()
        };
        var handler = new JsonWebTokenHandler();
        Assert.True((await handler.ValidateTokenAsync(token, parameters)).IsValid);
        parameters.ValidIssuer = "other";
        Assert.False((await handler.ValidateTokenAsync(token, parameters)).IsValid);
        parameters.ValidIssuer = "issuer"; parameters.ValidAudience = "other";
        Assert.False((await handler.ValidateTokenAsync(token, parameters)).IsValid);
        parameters.ValidAudience = "audience";
        using var other = RSA.Create(2048);
        parameters.IssuerSigningKeys = [new RsaSecurityKey(other)];
        Assert.False((await handler.ValidateTokenAsync(token, parameters)).IsValid);
    }

    [Fact]
    public async Task ExpiredAndTamperedTokensAreRejected()
    {
        using var rsa = RSA.Create(2048);
        var options = Microsoft.Extensions.Options.Options.Create(Options(rsa));
        using var keys = new SigningKeyProvider(options);
        var expired = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "issuer", Audience = "audience", IssuedAt = DateTime.UtcNow.AddHours(-2),
            NotBefore = DateTime.UtcNow.AddHours(-2), Expires = DateTime.UtcNow.AddHours(-1),
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.RsaSha256)
        });
        var parameters = new TokenValidationParameters { ValidIssuer = "issuer", ValidAudience = "audience", IssuerSigningKey = keys.Key };
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(expired, parameters)).IsValid);
        var token = new AccessTokenIssuer(keys, options, TimeProvider.System).Issue(Guid.NewGuid().ToString(), "USER");
        var parts = token.Split('.');
        parts[1] = Base64UrlEncoder.Encode("{\"sub\":\"intruder\"}");
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(string.Join('.', parts), parameters)).IsValid);
    }

    [Fact]
    public void InvalidPrivateKeyFailsImmediately()
    {
        Assert.Throws<InvalidOperationException>(() => new SigningKeyProvider(
            Microsoft.Extensions.Options.Options.Create(new SecurityOptions { SigningKeyBase64 = "invalid" })));
    }
}
