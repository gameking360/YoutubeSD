using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Upload.Messaging.Persistence;

namespace MediaProcessor.Data;

public enum JobStatus
{
    RUNNING,
    DONE,
    FAILED
}

/// <summary>Um vídeo em processamento: tentativas somadas entre réplicas e quem fez cada uma (F2/F8).</summary>
public sealed class ProcessingJob : IVersioned
{
    public string VideoId { get; set; } = "";
    public string LastMessageId { get; set; } = "";
    public int Attempts { get; set; }
    public JobStatus Status { get; set; } = JobStatus.RUNNING;
    public string InstanceId { get; set; } = "";
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? LastError { get; set; }
    public int Version { get; set; }
}

/// <summary>
/// Sobrepõe PROCESSOR_LATENCY_MS e PROCESSOR_FAILURE_RATE em tempo de execução, para todas as
/// réplicas de uma vez (cenários F1 e F7 ao vivo). NULL = usar a variável de ambiente.
/// </summary>
public sealed class ChaosSettings
{
    public int Id { get; set; }
    public int? LatencyMs { get; set; }
    public double? FailureRate { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Banco media_db, com migrations versionadas uma por tabela.</summary>
public sealed class MediaDbContext(DbContextOptions<MediaDbContext> options) : MessagingDbContext(options)
{
    public DbSet<ProcessingJob> Jobs => Set<ProcessingJob>();
    public DbSet<ChaosSettings> Chaos => Set<ChaosSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProcessingJob>(entity =>
        {
            entity.ToTable("processing_job");
            entity.HasKey(job => job.VideoId);
            entity.Property(job => job.VideoId).HasMaxLength(64);
            entity.Property(job => job.LastMessageId).HasMaxLength(64);
            entity.Property(job => job.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(job => job.InstanceId).HasMaxLength(64);
            entity.Property(job => job.LastError).HasMaxLength(500);
            entity.Property(job => job.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<ChaosSettings>(entity =>
        {
            entity.ToTable("chaos_settings");
            entity.HasKey(chaos => chaos.Id);
            entity.Property(chaos => chaos.Id).ValueGeneratedNever();
            entity.HasData(new ChaosSettings
            {
                Id = 1,
                LatencyMs = null,
                FailureRate = null,
                UpdatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        });
    }
}

/// <summary>Usado só pelo "dotnet ef" (ver UploadDbContextFactory).</summary>
public sealed class MediaDbContextFactory : IDesignTimeDbContextFactory<MediaDbContext>
{
    public MediaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__mediaDb")
            ?? "Server=localhost;Port=3306;User ID=root;Password=root123;Database=media_db";

        var builder = new DbContextOptionsBuilder<MediaDbContext>();
        builder.UseYoutubeMySql(connectionString);
        builder.ReplaceService<IModelCustomizer, TableScopedModelCustomizer>();
        return new MediaDbContext(builder.Options);
    }
}
