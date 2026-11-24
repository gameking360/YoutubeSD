using System.Text.Json;
using Upload.Messaging.Kafka;
using Upload.Messaging.Persistence;

namespace Upload.Messaging.Outbox;

public static class OutboxWriter
{
    /// <summary>
    /// Só adiciona a mensagem ao contexto: ela é gravada no mesmo SaveChanges da mudança
    /// de estado e publicada depois pelo OutboxPublisher (F6).
    /// </summary>
    public static string Enqueue(
        MessagingDbContext context,
        string topic,
        string messageKey,
        string type,
        string correlationId,
        object payload,
        DateTime now)
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new
        {
            messageId,
            correlationId,
            type,
            version = 1,
            occurredAt = now,
            payload
        };

        context.Outbox.Add(new OutboxMessage
        {
            MessageId = messageId,
            Topic = topic,
            MessageKey = messageKey,
            Type = type,
            Payload = JsonSerializer.Serialize(envelope, MessageEnvelope.JsonOptions),
            CorrelationId = correlationId,
            OccurredAt = now,
            CreatedAt = now
        });

        return messageId;
    }
}
