using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Upload.Messaging.Contracts;
using Upload.Messaging.Persistence;

namespace Upload.Data;

/// <summary>
/// Banco upload_db. O schema vem das migrations versionadas em Data/Migrations, uma por tabela,
/// como no Download (db/migration). Os tipos seguem o video_catalog do Download.
/// </summary>
public sealed class UploadDbContext(DbContextOptions<UploadDbContext> options) : MessagingDbContext(options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DbSet<VideoUpload> Videos => Set<VideoUpload>();
    public DbSet<UploadPart> Parts => Set<UploadPart>();
    public DbSet<VideoHistory> History => Set<VideoHistory>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();
    public DbSet<StorageCleanup> StorageCleanups => Set<StorageCleanup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<VideoUpload>(entity =>
        {
            entity.ToTable("video_upload");
            entity.HasKey(video => video.VideoId);

            // Mesmos tipos do video_catalog do Download.
            entity.Property(video => video.VideoId).HasMaxLength(64);
            entity.Property(video => video.OwnerId).HasMaxLength(64);
            entity.Property(video => video.Title).HasMaxLength(255);
            entity.Property(video => video.Visibility).HasConversion<string>().HasMaxLength(20);
            entity.Property(video => video.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(video => video.HlsBucket).HasMaxLength(100);
            entity.Property(video => video.MasterKey).HasMaxLength(500);
            entity.Property(video => video.Renditions)
                .HasColumnType("json")
                .HasConversion(
                    renditions => renditions == null ? null : JsonSerializer.Serialize(renditions, Json),
                    json => json == null ? null : JsonSerializer.Deserialize<List<Rendition>>(json, Json),
                    new ValueComparer<List<Rendition>?>(
                        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
                        renditions => renditions == null ? 0 : renditions.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                        renditions => renditions == null ? null : renditions.ToList()));
            entity.Property(video => video.ThumbnailKey).HasMaxLength(500);

            entity.Property(video => video.FileName).HasMaxLength(255);
            entity.Property(video => video.Extension).HasMaxLength(10);
            entity.Property(video => video.ContentType).HasMaxLength(100);
            entity.Property(video => video.RawBucket).HasMaxLength(100);
            entity.Property(video => video.RawKey).HasMaxLength(500);
            entity.Property(video => video.S3UploadId).HasColumnName("s3_upload_id").HasMaxLength(255);

            entity.Property(video => video.UploadState).HasConversion<string>().HasMaxLength(20);
            entity.Property(video => video.QuotaState).HasConversion<string>().HasMaxLength(20);
            entity.Property(video => video.ProcessingState).HasConversion<string>().HasMaxLength(20);
            entity.Property(video => video.FailureReason).HasMaxLength(60);
            entity.Property(video => video.Version).IsConcurrencyToken();

            entity.HasIndex(video => new { video.OwnerId, video.CreatedAt }).HasDatabaseName("idx_video_upload_dono");
            entity.HasIndex(video => new { video.Status, video.ProcessingState, video.DeadlineAt }).HasDatabaseName("idx_video_upload_prazos");
        });

        modelBuilder.Entity<UploadPart>(entity =>
        {
            entity.ToTable("upload_part");
            entity.HasKey(part => new { part.VideoId, part.PartNumber });
            entity.Property(part => part.VideoId).HasMaxLength(64);
            entity.Property(part => part.ETag).HasColumnName("etag").HasMaxLength(100);
            entity.HasOne<VideoUpload>()
                .WithMany()
                .HasForeignKey(part => part.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VideoHistory>(entity =>
        {
            entity.ToTable("video_history");
            entity.HasKey(history => history.Id);
            entity.Property(history => history.VideoId).HasMaxLength(64);
            entity.Property(history => history.FromStatus).HasMaxLength(20);
            entity.Property(history => history.ToStatus).HasMaxLength(20);
            entity.Property(history => history.Reason).HasMaxLength(60);
            entity.Property(history => history.Detail).HasMaxLength(500);
            entity.Property(history => history.MessageId).HasMaxLength(64);
            entity.Property(history => history.InstanceId).HasMaxLength(64);
            entity.HasIndex(history => new { history.VideoId, history.At }).HasDatabaseName("idx_video_history_video");
        });

        modelBuilder.Entity<UserNotification>(entity =>
        {
            entity.ToTable("user_notification");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.UserId).HasMaxLength(64);
            entity.Property(notification => notification.VideoId).HasMaxLength(64);
            entity.Property(notification => notification.Type).HasMaxLength(40);
            entity.Property(notification => notification.Message).HasMaxLength(500);
            entity.HasIndex(notification => new { notification.UserId, notification.CreatedAt }).HasDatabaseName("idx_user_notification_usuario");
        });

        modelBuilder.Entity<StorageCleanup>(entity =>
        {
            entity.ToTable("storage_cleanup");
            entity.HasKey(cleanup => cleanup.Id);
            entity.Property(cleanup => cleanup.VideoId).HasMaxLength(64);
            entity.Property(cleanup => cleanup.Action).HasConversion<string>().HasMaxLength(20);
            entity.Property(cleanup => cleanup.Bucket).HasMaxLength(100);
            entity.Property(cleanup => cleanup.KeyOrPrefix).HasMaxLength(500);
            entity.Property(cleanup => cleanup.UploadId).HasMaxLength(255);
            entity.Property(cleanup => cleanup.Reason).HasMaxLength(60);
            entity.Property(cleanup => cleanup.LastError).HasMaxLength(500);
            entity.Property(cleanup => cleanup.LockedBy).HasMaxLength(64);
            entity.Property(cleanup => cleanup.Version).IsConcurrencyToken();
            entity.HasIndex(cleanup => new { cleanup.DoneAt, cleanup.NotBefore }).HasDatabaseName("idx_storage_cleanup_pendentes");
        });
    }
}
