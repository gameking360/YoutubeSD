using Amazon.S3;
using Upload.Api;
using Upload.Configuration;
using Upload.Data;
using Upload.Messaging;
using Upload.Messaging.Kafka;
using Upload.Messaging.Outbox;
using Upload.Messaging.Persistence;
using Upload.Messaging.Storage;
using Upload.Saga;
using Upload.Storage;
using YoutubeSD.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddJwtAuthentication();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Configuração: endpoints, credenciais e tópicos vêm só de appsettings/variáveis de ambiente.
var uploadOptions = builder.Configuration.GetSection("Upload").Get<UploadOptions>() ?? new UploadOptions();
var kafka = KafkaSettings.Resolve(builder.Configuration, defaultGroup: "upload");
var s3 = S3Settings.Resolve(builder.Configuration);
var connectionString = MySqlDatabase.ResolveConnectionString(builder.Configuration, "uploadDb", "UPLOAD_DB_CONNECTION_STRING");

builder.Services.AddSingleton(uploadOptions);
builder.Services.AddSingleton(kafka);
builder.Services.AddSingleton(kafka.Topics);
builder.Services.AddSingleton(s3);
builder.Services.AddSingleton<IAmazonS3>(_ => s3.CreateClient());
builder.Services.AddKafkaProducer(kafka, "upload");
builder.Services.AddDbContextFactory<UploadDbContext>(options => options.UseYoutubeMySql(connectionString));

builder.Services.AddSingleton(new VideoSaga(uploadOptions, s3.HlsBucket));
builder.Services.AddSingleton<VideoSagaService>();
builder.Services.AddSingleton<RawStorage>();
builder.Services.AddSingleton<PartUploadThrottle>();

builder.Services.AddHostedService<OutboxPublisher<UploadDbContext>>();
builder.Services.AddHostedService<SagaEventsConsumer>();
builder.Services.AddHostedService<SagaTimeoutWatcher>();
builder.Services.AddHostedService<StorageTaskWorker>();

// Kafka e S3 ficam de fora de propósito: sem o Kafka o outbox segura as mensagens (F6), e um S3
// lento (montando um vídeo grande) não pode tirar todas as réplicas do gateway de uma vez. Erros
// de S3 viram 503 + Retry-After na própria requisição, e o cliente repete.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck<UploadDbContext>>("mysql");

// Cada requisição carrega no máximo uma parte.
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = uploadOptions.ChunkSizeBytes + UploadOptions.MiB);

var app = builder.Build();

await app.Services.MigrateDatabaseAsync<UploadDbContext>(app.Lifetime.ApplicationStopping);

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Instance-Id"] = InstanceInfo.Id;
    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
UploadEndpoints.Map(app);

app.Run();
