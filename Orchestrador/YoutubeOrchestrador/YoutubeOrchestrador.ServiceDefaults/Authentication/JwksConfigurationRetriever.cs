using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace YoutubeSD.Authentication;

public sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var json = await retriever.GetDocumentAsync(address, cancel);
        var configuration = new OpenIdConnectConfiguration { JwksUri = address };
        foreach (var key in new JsonWebKeySet(json).GetSigningKeys())
            configuration.SigningKeys.Add(key);
        if (configuration.SigningKeys.Count == 0)
            throw new InvalidOperationException("JWKS não contém chaves de assinatura válidas.");
        return configuration;
    }
}
