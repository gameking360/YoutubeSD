using Confluent.Kafka;
using identity.Api;
using identity.Configuration;
using identity.Data;
using identity.Messaging;
using identity.Observability;
using identity.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IdentityDatabase>();
builder.Services.AddSingleton(KafkaOptions.Resolve(builder.Configuration));
builder.Services.AddSingleton<IProducer<string, string>>(services =>
{
    var kafka = services.GetRequiredService<KafkaOptions>();
    var config = new ProducerConfig
    {
        BootstrapServers = kafka.BootstrapServers,
        ClientId = $"identity-{InstanceInfo.Id}-producer",
        Acks = Acks.All,
        EnableIdempotence = true,
        MessageTimeoutMs = 5000,
        AllowAutoCreateTopics = false
    };

    return new ProducerBuilder<string, string>(config).Build();
});

builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<QuotaService>();
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddHealthChecks().AddCheck<MySqlHealthCheck>("mysql");
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<QuotaEventsConsumer>();

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapIdentityEndpoints();

app.Run();
