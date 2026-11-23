var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();
// Internal Identity APIs are reachable only through service discovery, never through the public gateway.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/identity/internal", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next(context);
});
app.MapReverseProxy();

app.Run();
