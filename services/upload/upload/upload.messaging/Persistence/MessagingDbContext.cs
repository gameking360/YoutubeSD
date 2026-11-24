using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Upload.Messaging.Persistence;

/// <summary>
/// Base dos DbContexts do Upload e do media-processor: traz as tabelas de mensageria
/// (processed_messages, outbox e leases) e as convenções de tipo usadas pelo Download
/// (datas em DATETIME(3) e UTC).
/// </summary>
public abstract class MessagingDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<Lease> Leases => Set<Lease>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("datetime(3)")
            .HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyMessagingModel();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Concorrência otimista: toda linha versionada alterada sobe a versão, e o UPDATE só
    /// acontece se a versão no banco ainda for a lida. Quem perder recebe DbUpdateConcurrencyException.
    /// </summary>
    private void BumpVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Version++;
            }
        }
    }
}

/// <summary>Grava e lê as datas sempre como UTC.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
