using MediaProcessor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MySql;
using Upload.Data;
using Upload.Domain;
using Upload.Messaging.Kafka;
using Upload.Messaging.Persistence;

namespace Upload.Tests;

/// <summary>MySQL descartável (precisa do Docker), como os testes do Security e do Download.</summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:9.7")
        .WithDatabase("upload_tests")
        .WithUsername("root")
        .WithPassword("upload-tests-only")
        .Build();

    public string ConnectionString(string database) =>
        new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(_mysql.GetConnectionString())
        {
            Database = database,
            AllowPublicKeyRetrieval = true
        }.ConnectionString;

    public Task InitializeAsync() => _mysql.StartAsync();

    public Task DisposeAsync() => _mysql.DisposeAsync().AsTask();
}

public sealed class DatabaseTests(MySqlFixture mysql) : IClassFixture<MySqlFixture>
{
    [Fact]
    public async Task Migrations_criam_os_dois_bancos_do_zero_e_sao_idempotentes()
    {
        var upload = Services<UploadDbContext>(mysql.ConnectionString("upload_db_migrations"));
        await upload.MigrateDatabaseAsync<UploadDbContext>(CancellationToken.None);
        await upload.MigrateDatabaseAsync<UploadDbContext>(CancellationToken.None);

        var media = Services<MediaDbContext>(mysql.ConnectionString("media_db_migrations"));
        await media.MigrateDatabaseAsync<MediaDbContext>(CancellationToken.None);

        await using var uploadContext = await upload.GetRequiredService<IDbContextFactory<UploadDbContext>>().CreateDbContextAsync();
        Assert.Equal(8, (await uploadContext.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await uploadContext.Database.GetPendingMigrationsAsync());
        Assert.True(await uploadContext.Leases.AnyAsync(lease => lease.Name == MessagingModel.OutboxLease));

        await using var mediaContext = await media.GetRequiredService<IDbContextFactory<MediaDbContext>>().CreateDbContextAsync();
        Assert.Equal(5, (await mediaContext.Database.GetAppliedMigrationsAsync()).Count());
        Assert.True(await mediaContext.Chaos.AnyAsync(chaos => chaos.Id == 1));
    }

    [Fact]
    public async Task Processed_messages_repetido_e_detectado_como_duplicata()
    {
        var factory = await UploadFactoryAsync("upload_db_duplicates");
        var message = MessageEnvelope.Parse(
            $$$"""{"messageId":"{{{Guid.NewGuid()}}}","correlationId":"v","type":"QuotaReserved","version":1,"occurredAt":"2026-11-24T00:00:00Z","payload":{"videoId":"v"}}""");

        await using (var first = await factory.CreateDbContextAsync())
        {
            Idempotency.Register(first, message, DateTime.UtcNow);
            await first.SaveChangesAsync();
        }

        await using var second = await factory.CreateDbContextAsync();
        Assert.True(await Idempotency.AlreadyProcessedAsync(second, message.MessageId, CancellationToken.None));
        Idempotency.Register(second, message, DateTime.UtcNow);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.True(DbErrors.IsDuplicateKey(error));
    }

    [Fact]
    public async Task Duas_replicas_alterando_o_mesmo_video_geram_conflito_de_concorrencia()
    {
        var factory = await UploadFactoryAsync("upload_db_concurrency");
        var videoId = Guid.NewGuid().ToString();
        await using (var setup = await factory.CreateDbContextAsync())
        {
            setup.Videos.Add(new VideoUpload
            {
                VideoId = videoId,
                OwnerId = "dono",
                Title = "Aula",
                FileName = "aula.mp4",
                Extension = "mp4",
                SizeBytes = 10,
                ChunkSize = 10,
                TotalParts = 1,
                RawBucket = "videos-raw",
                RawKey = $"{videoId}/original.mp4",
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Renditions = [new("360p", 640, 360, 928000)]
            });
            await setup.SaveChangesAsync();
        }

        await using var replicaA = await factory.CreateDbContextAsync();
        await using var replicaB = await factory.CreateDbContextAsync();
        var videoA = await replicaA.Videos.SingleAsync(v => v.VideoId == videoId);
        var videoB = await replicaB.Videos.SingleAsync(v => v.VideoId == videoId);

        videoA.QuotaState = QuotaState.RESERVED;
        videoA.UpdatedAt = DateTime.UtcNow;
        await replicaA.SaveChangesAsync();

        videoB.UploadState = UploadState.COMPLETED;
        videoB.UpdatedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => replicaB.SaveChangesAsync());

        await using var check = await factory.CreateDbContextAsync();
        var stored = await check.Videos.SingleAsync(v => v.VideoId == videoId);
        Assert.Equal(1, stored.Version);
        Assert.Equal(QuotaState.RESERVED, stored.QuotaState);
        Assert.Equal("360p", Assert.Single(stored.Renditions!).Name);
    }

    [Fact]
    public async Task OptimisticRetry_rele_e_decide_de_novo()
    {
        var factory = await UploadFactoryAsync("upload_db_retry");
        var calls = 0;

        await OptimisticRetry.RunAsync(factory, async context =>
        {
            calls++;
            var lease = await context.Leases.SingleAsync(l => l.Name == MessagingModel.OutboxLease);
            lease.Owner = $"tentativa-{calls}";

            if (calls == 1)
            {
                // Outra "réplica" altera a linha entre a leitura e a escrita.
                await using var other = await factory.CreateDbContextAsync();
                var competing = await other.Leases.SingleAsync(l => l.Name == MessagingModel.OutboxLease);
                competing.Owner = "outra-replica";
                await other.SaveChangesAsync();
            }

            await context.SaveChangesAsync();
        }, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, calls);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal("tentativa-2", (await check.Leases.SingleAsync(l => l.Name == MessagingModel.OutboxLease)).Owner);
    }

    private async Task<IDbContextFactory<UploadDbContext>> UploadFactoryAsync(string database)
    {
        var services = Services<UploadDbContext>(mysql.ConnectionString(database));
        await services.MigrateDatabaseAsync<UploadDbContext>(CancellationToken.None);
        return services.GetRequiredService<IDbContextFactory<UploadDbContext>>();
    }

    private static ServiceProvider Services<TContext>(string connectionString) where TContext : DbContext =>
        new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .AddDbContextFactory<TContext>(options => options.UseYoutubeMySql(connectionString))
            .BuildServiceProvider();
}
