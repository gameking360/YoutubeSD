using YoutubeSD.Security.Security;
using YoutubeSD.Security.Services;

namespace YoutubeSD.Security.Api;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", async (LoginRequest request, HttpContext context, AuthenticationService auth, CancellationToken ct) =>
        {
            NoStore(context);
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 255
                || string.IsNullOrEmpty(request.Password) || request.Password.Length > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Informe e-mail e senha válidos."] });
            var response = await auth.LoginAsync(request.Email.Trim().ToLowerInvariant(), request.Password, ct);
            return response is null ? Results.Problem(statusCode: 401, title: "Credenciais inválidas") : Results.Ok(response);
        });
        group.MapPost("/refresh", async (RefreshRequest request, HttpContext context, AuthenticationService auth, CancellationToken ct) =>
        {
            NoStore(context);
            if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 256)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["refreshToken"] = ["Informe o refresh token."] });
            var response = await auth.RefreshAsync(request.RefreshToken, ct);
            return response is null ? Results.Problem(statusCode: 401, title: "Sessão inválida ou expirada") : Results.Ok(response);
        });
        group.MapGet("/.well-known/jwks.json", (HttpContext context, SigningKeyProvider keys) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Ok(keys.PublicJwks());
        });
        return app;
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
