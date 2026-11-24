using Microsoft.EntityFrameworkCore;

namespace Upload.Messaging.Persistence;

public static class MessagingModel
{
    public const string OutboxLease = "outbox";

    /// <summary>Tabelas de mensageria com os mesmos tipos usados pelo Download.</summary>
    public static ModelBuilder ApplyMessagingModel(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.ToTable("processed_messages");
            entity.HasKey(message => message.MessageId);
            entity.Property(message => message.MessageId).HasMaxLength(64);
            entity.Property(message => message.Type).HasMaxLength(64);
            entity.Property(message => message.ProcessedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(3)");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).ValueGeneratedOnAdd();
            entity.Property(message => message.MessageId).HasMaxLength(64);
            entity.Property(message => message.Topic).HasMaxLength(120);
            entity.Property(message => message.MessageKey).HasMaxLength(64);
            entity.Property(message => message.Type).HasMaxLength(64);
            entity.Property(message => message.Payload).HasColumnType("json");
            entity.Property(message => message.CorrelationId).HasMaxLength(64);
            entity.Property(message => message.LastError).HasMaxLength(500);
            entity.HasIndex(message => message.MessageId).IsUnique().HasDatabaseName("idx_outbox_message_id");
            entity.HasIndex(message => new { message.PublishedAt, message.Id }).HasDatabaseName("idx_outbox_pendentes");
        });

        modelBuilder.Entity<Lease>(entity =>
        {
            entity.ToTable("leases");
            entity.HasKey(lease => lease.Name);
            entity.Property(lease => lease.Name).HasMaxLength(64);
            entity.Property(lease => lease.Owner).HasMaxLength(64);
            entity.Property(lease => lease.Version).IsConcurrencyToken();
            entity.HasData(new Lease
            {
                Name = OutboxLease,
                Owner = null,
                ExpiresAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Version = 0
            });
        });

        return modelBuilder;
    }
}
