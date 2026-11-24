namespace YoutubeSD.Authentication;

public sealed class JwtOptions
{
    public string JwksUri { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public bool RequireHttpsMetadata { get; set; } = true;
}
