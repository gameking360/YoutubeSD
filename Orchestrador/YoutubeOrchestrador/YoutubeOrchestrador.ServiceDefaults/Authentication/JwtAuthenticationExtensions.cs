using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace YoutubeSD.Authentication;

public static class JwtAuthenticationExtensions
{
    public static TBuilder AddJwtAuthentication<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<JwtOptions>().BindConfiguration("Jwt")
            .Validate(o => Uri.TryCreate(o.JwksUri, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" && (!o.RequireHttpsMetadata || uri.Scheme == "https"), "Jwt:JwksUri inválida.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "Issuer e Audience obrigatórios.")
            .ValidateOnStart();
        builder.Services.AddHttpClient("jwks", http => http.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, IHttpClientFactory>((bearer, configuration, clients) =>
            {
                var jwt = configuration.Value;
                bearer.MapInboundClaims = false;
                bearer.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                bearer.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(jwt.JwksUri,
                    new JwksConfigurationRetriever(), new HttpDocumentRetriever(clients.CreateClient("jwks"))
                    { RequireHttps = jwt.RequireHttpsMetadata })
                {
                    AutomaticRefreshInterval = TimeSpan.FromHours(1),
                    RefreshInterval = TimeSpan.FromMinutes(1)
                };
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                    ValidateLifetime = true, RequireExpirationTime = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = "sub", RoleClaimType = "roles",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        builder.Services.AddAuthorization();
        return builder;
    }
}
