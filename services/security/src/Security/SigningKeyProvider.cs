using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using YoutubeSD.Security.Configuration;

namespace YoutubeSD.Security.Security;

public sealed class SigningKeyProvider : IDisposable
{
    private readonly RSA _rsa = RSA.Create();
    public RsaSecurityKey Key { get; }

    public SigningKeyProvider(IOptions<SecurityOptions> options)
    {
        try
        {
            var bytes = Convert.FromBase64String(options.Value.SigningKeyBase64);
            _rsa.ImportPkcs8PrivateKey(bytes, out var read);
            if (read != bytes.Length || _rsa.KeySize < 2048)
                throw new CryptographicException("Chave RSA precisa ter pelo menos 2048 bits.");
            var kid = Base64UrlEncoder.Encode(SHA256.HashData(_rsa.ExportSubjectPublicKeyInfo()));
            Key = new RsaSecurityKey(_rsa) { KeyId = kid };
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            _rsa.Dispose();
            throw new InvalidOperationException("Security:SigningKeyBase64 deve conter uma chave privada RSA PKCS#8 válida.", ex);
        }
    }

    public object PublicJwks()
    {
        var parameters = _rsa.ExportParameters(false);
        return new { keys = new[] { new {
            kty = "RSA", use = "sig", alg = "RS256", kid = Key.KeyId,
            n = Base64UrlEncoder.Encode(parameters.Modulus!),
            e = Base64UrlEncoder.Encode(parameters.Exponent!)
        } } };
    }

    public void Dispose() => _rsa.Dispose();
}
