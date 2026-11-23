using identity.Services;

namespace identity.Api;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity");

        group.MapPost("/users", RegisterAsync);
        group.MapGet("/users/me", GetMeAsync);
        group.MapPut("/users/me", UpdateMeAsync);
        group.MapGet("/users/{id:guid}", GetByIdAsync);
        group.MapGet("/quota/me", GetMyQuotaAsync);
        group.MapPost("/internal/authenticate", AuthenticateAsync);
        group.MapPost("/admin/users/{id:guid}/quota/reset", ResetQuotaAsync);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserService users,
        CancellationToken cancellationToken)
    {
        var errors = ValidateRegistration(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var name = request.Name!.Trim();
        var email = request.Email!.Trim().ToLowerInvariant();

        try
        {
            var user = await users.CreateAsync(name, email, request.Password!, cancellationToken);
            return Results.Created(
                $"/api/identity/users/{user.Id}",
                new UserCreatedResponse(user.Id, user.Name, user.Email, user.Role, user.CreatedAt));
        }
        catch (DuplicateEmailException)
        {
            return Results.Problem(
                title: "E-mail já cadastrado",
                detail: "Já existe um usuário com este e-mail.",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> GetByIdAsync(
        string id,
        UserService users,
        CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(id, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext context,
        UserService users,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.Id(context);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static async Task<IResult> UpdateMeAsync(
        UpdateProfileRequest request,
        HttpContext context,
        UserService users,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.Id(context);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Informe um nome com até 120 caracteres."]
            });
        }

        var user = await users.UpdateNameAsync(userId, request.Name.Trim(), cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static async Task<IResult> GetMyQuotaAsync(
        HttpContext context,
        QuotaService quotas,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUser.Id(context);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var quota = await quotas.GetAsync(userId, cancellationToken);
        return quota is null ? Results.NotFound() : Results.Ok(quota);
    }

    private static async Task<IResult> AuthenticateAsync(
        AuthenticateRequest request,
        HttpContext context,
        UserService users,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!InternalKeyAllowed(context, configuration))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Informe e-mail e senha."]
            });
        }

        var user = await users.AuthenticateAsync(
            request.Email.Trim().ToLowerInvariant(), request.Password, cancellationToken);

        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }

    private static async Task<IResult> ResetQuotaAsync(
        string id,
        HttpContext context,
        QuotaService quotas,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!AdminKeyAllowed(context, configuration))
        {
            return Results.Unauthorized();
        }

        var quota = await quotas.ResetAsync(id, cancellationToken);
        return quota is null ? Results.NotFound() : Results.Ok(quota);
    }

    private static Dictionary<string, string[]> ValidateRegistration(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            errors["name"] = ["Informe um nome com até 120 caracteres."];
        }

        var email = request.Email?.Trim() ?? "";
        if (!IsEmail(email))
        {
            errors["email"] = ["Informe um e-mail válido."];
        }

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 6 || request.Password.Length > 100)
        {
            errors["password"] = ["A senha precisa ter entre 6 e 100 caracteres."];
        }

        return errors;
    }

    private static bool IsEmail(string email)
    {
        if (email.Length is 0 or > 255)
        {
            return false;
        }

        return System.Net.Mail.MailAddress.TryCreate(email, out var address)
            && string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
    }

    private static bool InternalKeyAllowed(HttpContext context, IConfiguration configuration)
    {
        var expected = configuration["Identity:InternalApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return string.Equals(
            expected,
            context.Request.Headers["X-Internal-Key"].FirstOrDefault(),
            StringComparison.Ordinal);
    }

    private static bool AdminKeyAllowed(HttpContext context, IConfiguration configuration)
    {
        var expected = configuration["Identity:AdminToken"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return string.Equals(
            expected,
            context.Request.Headers["X-Admin-Token"].FirstOrDefault(),
            StringComparison.Ordinal);
    }
}
