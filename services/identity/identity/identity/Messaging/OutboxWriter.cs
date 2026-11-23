using System.Data;
using System.Text.Json;
using Dapper;

namespace identity.Messaging;

public static class OutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string> EnqueueAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string topic,
        string messageKey,
        string type,
        string correlationId,
        object payload,
        CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid().ToString();
        var occurredAt = DateTime.UtcNow;
        var envelope = new
        {
            messageId,
            correlationId,
            type,
            version = 1,
            occurredAt,
            payload
        };

        await connection.ExecuteAsync(
            """
            INSERT INTO outbox (message_id, topic, message_key, type, payload, correlation_id, occurred_at, created_at)
            VALUES (@MessageId, @Topic, @MessageKey, @Type, @Payload, @CorrelationId, @OccurredAt, @CreatedAt)
            """,
            new
            {
                MessageId = messageId,
                Topic = topic,
                MessageKey = messageKey,
                Type = type,
                Payload = JsonSerializer.Serialize(envelope, JsonOptions),
                CorrelationId = correlationId,
                OccurredAt = occurredAt,
                CreatedAt = occurredAt
            },
            transaction);

        return messageId;
    }
}
