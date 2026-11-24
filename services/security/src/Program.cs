using System.Data.Common;
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using YoutubeSD.Security.Api;
using YoutubeSD.Security.Configuration;
using YoutubeSD.Security.Data;
using YoutubeSD.Security.Integration;
using YoutubeSD.Security.Security;
using YoutubeSD.Security.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOptions<SecurityOptions>().BindConfiguration("Security")
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKeyBase64), "Chave de assinatura obrigatória.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "Issuer e Audience obrigatórios.")
    .Validate(o => o.AccessTokenMinutes is >= 1 and <= 60 && o.RefreshTokenDays is >= 1 and <= 90, "Duração dos tokens inválida.")
    .Validate(o => Uri.TryCreate(o.IdentityBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", "URL do Identity inválida.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.IdentityInternalApiKey) && o.IdentityTimeoutSeconds is >= 1 and <= 30, "Configuração do Identity inválida.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SigningKeyProvider>();
builder.Services.AddSingleton<AccessTokenIssuer>();
builder.Services.AddSingleton<SecurityDatabase>();
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddScoped<RefreshTokenRepository>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddHttpClient<IdentityClient>((services, http) =>
{
    var options = services.GetRequiredService<IOptions<SecurityOptions>>().Value;
    http.BaseAddress = new Uri(options.IdentityBaseUrl);
    http.Timeout = TimeSpan.FromSeconds(options.IdentityTimeoutSeconds);
    http.DefaultRequestHeaders.Add("X-Internal-Key", options.IdentityInternalApiKey);
});
builder.Services.AddHealthChecks().AddCheck<MySqlHealthCheck>("mysql");
var app = builder.Build();
_ = app.Services.GetRequiredService<SigningKeyProvider>();
await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);
app.Use(async (context, next) =>
{
    using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["instancia"] = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Environment.MachineName,
        ["correlationId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
    });
    await next(context);
});
app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var unavailable = exception is IdentityUnavailableException or DbException;
    await Results.Problem(statusCode: unavailable ? 503 : 500,
        title: unavailable ? "Dependência indisponível. Tente novamente." : "Erro ao processar a requisição.").ExecuteAsync(context);
}));
app.MapDefaultEndpoints();
app.MapAuthEndpoints();
app.Run();

public partial class Program;
