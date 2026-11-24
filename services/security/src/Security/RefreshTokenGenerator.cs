using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace YoutubeSD.Security.Security;

public static class RefreshTokenGenerator
{
    public static string Generate() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
