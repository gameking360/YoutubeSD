using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Upload.Messaging.Persistence;

namespace Upload.Messaging.Outbox;

/// <summary>
/// Publica o outbox no Kafka. Todas as réplicas rodam este serviço, mas só uma publica por vez:
/// o líder é eleito por um lease em tabela, gravado com concurrency token. Isso mantém a ordem
/// por id. Se o líder morre, o lease expira e outra réplica assume. Com o Kafka fora do ar, as
/// mensagens ficam pendentes e saem quando ele volta (F6).
/// </summary>
public sealed class OutboxPublisher<TContext>(
    IDbContextFactory<TContext> factory,
    IProducer<string, string> producer,
    ILogger<OutboxPublisher<TContext>> logger) : BackgroundService
    where TContext : MessagingDbContext
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(15);
    private const int BatchSize = 50;

    private readonly string _owner = $"{InstanceInfo.Id}-{Guid.NewGuid().ToString("N")[..8]}";
    private DateTime _leaseValidUntil = DateTime.MinValue;
    private bool _isLeader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await TryHoldLeaseAsync(stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (!await PublishBatchAsync(stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    "Falha no publicador do outbox; nova tentativa em 2s. erro={Error} instancia={InstanceId}",
                    ex.GetBaseException().Message, InstanceInfo.Id);
                await SafeDelayAsync(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        await ReleaseLeaseAsync();

        try
        {
            producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao esvaziar o produtor Kafka.");
        }
    }

    private async Task<bool> TryHoldLeaseAsync(CancellationToken cancellationToken)
    {
        if (_isLeader && DateTime.UtcNow < _leaseValidUntil - TimeSpan.FromSeconds(7))
        {
            // Lease renovado há pouco: evita uma escrita no banco a cada lote.
            return true;
        }

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var lease = await context.Leases.SingleOrDefaultAsync(l => l.Name == MessagingModel.OutboxLease, cancellationToken);

        if (lease is null)
        {
            lease = new Lease { Name = MessagingModel.OutboxLease };
            context.Leases.Add(lease);
        }
        else if (lease.Owner != _owner && lease.ExpiresAt > now)
        {
            SetLeader(false, lease.Owner);
            return false;
        }

        lease.Owner = _owner;
        lease.ExpiresAt = now + LeaseDuration;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Outra réplica renovou ou assumiu o lease ao mesmo tempo (concurrency token ou chave).
            SetLeader(false, null);
            return false;
        }

        _leaseValidUntil = now + LeaseDuration - TimeSpan.FromSeconds(3);
        SetLeader(true, _owner);
        return true;
    }

    private async Task<bool> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Outbox
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return false;
        }

        foreach (var row in rows)
        {
            if (DateTime.UtcNow >= _leaseValidUntil)
            {
                // O lease está para vencer: renova antes de continuar, para não publicar sem ser o líder.
                break;
            }

            try
            {
                await producer.ProduceAsync(
                    row.Topic,
                    new Message<string, string> { Key = row.MessageKey, Value = row.Payload },
                    cancellationToken);

                row.PublishedAt = DateTime.UtcNow;
                row.Attempts++;
                row.LastError = null;
                await context.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Outbox publicada messageId={MessageId} correlationId={CorrelationId} tipo={Type} topico={Topic} instancia={InstanceId}",
                    row.MessageId, row.CorrelationId, row.Type, row.Topic, InstanceInfo.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                row.Attempts++;
                row.LastError = Truncate(ex.Message, 450);
                await context.SaveChangesAsync(CancellationToken.None);

                logger.LogWarning(
                    "Outbox pendente; Kafka indisponível? topico={Topic} messageId={MessageId} erro={Error} instancia={InstanceId}",
                    row.Topic, row.MessageId, ex.Message, InstanceInfo.Id);

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                break;
            }
        }

        return true;
    }

    private async Task ReleaseLeaseAsync()
    {
        if (!_isLeader)
        {
            return;
        }

        try
        {
            await using var context = await factory.CreateDbContextAsync();
            var lease = await context.Leases.SingleOrDefaultAsync(l => l.Name == MessagingModel.OutboxLease && l.Owner == _owner);
            if (lease is not null)
            {
                lease.ExpiresAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                logger.LogInformation("Lease do outbox liberado no desligamento. instancia={InstanceId}", InstanceInfo.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Não foi possível liberar o lease do outbox: {Error}", ex.GetBaseException().Message);
        }
    }

    private void SetLeader(bool leader, string? owner)
    {
        if (leader == _isLeader)
        {
            return;
        }

        _isLeader = leader;
        if (leader)
        {
            logger.LogInformation("Esta réplica assumiu a publicação do outbox. dono={Owner} instancia={InstanceId}", owner, InstanceInfo.Id);
        }
        else
        {
            logger.LogInformation("Publicação do outbox com outra réplica. dono={Owner} instancia={InstanceId}", owner, InstanceInfo.Id);
        }
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
