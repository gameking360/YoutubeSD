namespace identity.Api;

public sealed record RegisterRequest(string? Name, string? Email, string? Password);

public sealed record UpdateProfileRequest(string? Name);

public sealed record AuthenticateRequest(string? Email, string? Password);
public sealed record AuthenticationContextResponse(string UserId, string Role);

public sealed record QuotaResponse(int Limit, int Used, int Reserved, int Available);

public sealed record UserResponse(
    string Id,
    string Name,
    string Email,
    string Role,
    DateTime CreatedAt,
    QuotaResponse Quota);

public sealed record AuthenticatedUserResponse(string UserId, string Name, string Email, string Role);

public sealed record UserCreatedResponse(string Id, string Name, string Email, string Role, DateTime CreatedAt);
