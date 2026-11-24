using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MySql;
using YoutubeSD.Security.Integration;

namespace Security.Tests.Infrastructure;

[CollectionDefinition("security", DisableParallelization = true)]
public sealed class SecurityCollection : ICollectionFixture<SecurityApiFactory>;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Reset() => _now = DateTimeOffset.UtcNow;
    public void Advance(TimeSpan interval) => _now += interval;
}

public sealed class SecurityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:9.7")
        .WithDatabase("security_tests").WithUsername("root").WithPassword("security-tests-only").Build();
    private readonly RSA _rsa = RSA.Create(2048);
    public TestClock Clock { get; } = new();
    public string UserId { get; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = "USER";
    public HttpStatusCode? IdentityFailure { get; set; }
    public bool IdentityTimeout { get; set; }
    public string ConnectionString => _mysql.GetConnectionString();
    public string SigningKey => Convert.ToBase64String(_rsa.ExportPkcs8PrivateKey());
    public HttpClient Client => CreateClient();

    public async Task InitializeAsync() => await _mysql.StartAsync();
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _mysql.DisposeAsync();
        _rsa.Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Orchestrador"))) directory = directory.Parent;
        builder.UseContentRoot(Path.Combine(directory!.FullName, "services", "security", "src"));
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:securityDb"] = ConnectionString,
            ["Security:SigningKeyBase64"] = SigningKey,
            ["Security:IdentityBaseUrl"] = "http://identity.test",
            ["Security:IdentityInternalApiKey"] = "test-internal-key",
            ["Security:Issuer"] = "youtubesd-security",
            ["Security:Audience"] = "youtubesd-api"
        }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient<IdentityClient>().ConfigurePrimaryHttpMessageHandler(() => new FakeIdentityHandler(this));
        });
    }

    public void Reset()
    {
        Clock.Reset(); Role = "USER"; IdentityFailure = null; IdentityTimeout = false;
    }

    private sealed class FakeIdentityHandler(SecurityApiFactory factory) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("test-internal-key", request.Headers.GetValues("X-Internal-Key").Single());
            if (factory.IdentityTimeout) throw new TaskCanceledException("Simulated timeout");
            if (factory.IdentityFailure is { } failure) return new HttpResponseMessage(failure);
            if (request.Method == HttpMethod.Post)
            {
                var credentials = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
                if (credentials.GetProperty("email").GetString() != "ana@teste.local"
                    || credentials.GetProperty("password").GetString() != "senha123")
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { userId = factory.UserId, role = factory.Role })
            };
        }
    }
}
