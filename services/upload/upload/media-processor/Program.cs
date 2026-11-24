using Amazon.S3;
using MediaProcessor.Configuration;
using MediaProcessor.Data;
using MediaProcessor.Processing;
using Upload.Messaging;
using Upload.Messaging.Kafka;
using Upload.Messaging.Outbox;
using Upload.Messaging.Persistence;
using Upload.Messaging.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var options = ProcessorOptions.Resolve(builder.Configuration);
var kafka = KafkaSettings.Resolve(builder.Configuration, defaultGroup: "media-processor");
var s3 = S3Settings.Resolve(builder.Configuration);
var connectionString = MySqlDatabase.ResolveConnectionString(builder.Configuration, "mediaDb", "MEDIA_DB_CONNECTION_STRING");

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(kafka);
builder.Services.AddSingleton(kafka.Topics);
builder.Services.AddSingleton(s3);
builder.Services.AddSingleton<IAmazonS3>(_ => s3.CreateClient());
builder.Services.AddKafkaProducer(kafka, "media-processor");
builder.Services.AddDbContextFactory<MediaDbContext>(db => db.UseYoutubeMySql(connectionString));

builder.Services.AddSingleton<ChaosSettingsProvider>();
builder.Services.AddSingleton<MediaStorage>();
builder.Services.AddSingleton<MediaPipeline>();
builder.Services.AddSingleton<ProcessingService>();

builder.Services.AddHostedService<OutboxPublisher<MediaDbContext>>();
builder.Services.AddHostedService<VideoUploadedConsumer>();

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck<MediaDbContext>>("mysql");

var app = builder.Build();

await app.Services.MigrateDatabaseAsync<MediaDbContext>(app.Lifetime.ApplicationStopping);

var version = await ProcessRunner.RunAsync(options.FfmpegPath, ["-hide_banner", "-version"], app.Lifetime.ApplicationStopping)
    .ContinueWith(task => task.IsCompletedSuccessfully && task.Result.ExitCode == 0
        ? task.Result.StandardOutput.Split('\n').FirstOrDefault()?.Trim()
        : null);

app.Logger.LogInformation(
    "media-processor pronto: {Ffmpeg}; consumidores={Concurrency} threadsFfmpeg={Threads} tentativas={Attempts} latenciaMs={Latency} taxaFalha={FailureRate} instancia={InstanceId}",
    version ?? "FFMPEG NÃO ENCONTRADO", options.Concurrency, options.FfmpegThreads == 0 ? "auto" : options.FfmpegThreads.ToString(),
    options.MaxAttempts, options.LatencyMs, options.FailureRate, InstanceInfo.Id);

app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "media-processor", instance = InstanceInfo.Id }));

app.Run();
