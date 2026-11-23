namespace YoutubeSD.Security.Configuration;

public sealed class SecurityOptions
{
    public string SigningKeyBase64 { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public string IdentityBaseUrl { get; set; } = "http://identityService";
    public string IdentityInternalApiKey { get; set; } = "";
    public int IdentityTimeoutSeconds { get; set; } = 5;
}
