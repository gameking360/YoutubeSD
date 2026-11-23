using Confluent.Kafka;
using Dapper;
using identity.Data;
using identity.Observability;

namespace identity.Messaging;

public sealed class OutboxPublisher(
    IdentityDatabase database,
    IProducer<string, string> producer,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hasPending = await PublishBatchAsync(stoppingToken);
                if (!hasPending)
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
                    ex,
                    "Falha no publicador do outbox; nova tentativa em 2s. instancia={InstanceId}",
                    InstanceInfo.Id);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        try
        {
            producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao esvaziar o produtor Kafka.");
        }
    }

    private async Task<bool> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<OutboxRow>(
            """
            SELECT id AS Id,
                   message_id AS MessageId,
                   topic AS Topic,
                   message_key AS MessageKey,
                   payload AS Payload
            FROM outbox
            WHERE published_at IS NULL
            ORDER BY id
            LIMIT 50
            """)).AsList();

        if (rows.Count == 0)
        {
            return false;
        }

        foreach (var row in rows)
        {
            try
            {
                await producer.ProduceAsync(
                    row.Topic,
                    new Message<string, string> { Key = row.MessageKey, Value = row.Payload },
                    cancellationToken);

                await connection.ExecuteAsync(
                    "UPDATE outbox SET published_at = @Now, attempts = attempts + 1, last_error = NULL WHERE id = @Id",
                    new { Now = DateTime.UtcNow, row.Id });

                logger.LogInformation(
                    "Outbox publicada messageId={MessageId} topico={Topic} instancia={InstanceId}",
                    row.MessageId, row.Topic, InstanceInfo.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await connection.ExecuteAsync(
                    "UPDATE outbox SET attempts = attempts + 1, last_error = @Error WHERE id = @Id",
                    new { Error = Truncate(ex.Message, 450), row.Id });

                logger.LogWarning(
                    "Outbox pendente; Kafka indisponível? topico={Topic} messageId={MessageId} erro={Error} instancia={InstanceId}",
                    row.Topic, row.MessageId, ex.Message, InstanceInfo.Id);

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                break;
            }
        }

        return true;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed class OutboxRow
    {
        public long Id { get; set; }
        public Guid MessageId { get; set; }
        public string Topic { get; set; } = "";
        public string MessageKey { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
