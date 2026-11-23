using YoutubeSD.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddJwtAuthentication();
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

app.MapGet("/", () => "Hello World!");

app.Run();
