using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using YoutubeSD.Security.Configuration;

namespace YoutubeSD.Security.Security;

public sealed class AccessTokenIssuer(SigningKeyProvider keys, IOptions<SecurityOptions> options, TimeProvider clock)
{
    public int ExpiresIn => checked(options.Value.AccessTokenMinutes * 60);

    public string Issue(string userId, string role)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(ExpiresIn),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = userId,
                ["roles"] = new[] { role },
                ["jti"] = Guid.NewGuid().ToString()
            },
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.RsaSha256)
        });
    }
}
