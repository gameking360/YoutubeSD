namespace YoutubeSD.Security.Api;

public sealed record LoginRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);
