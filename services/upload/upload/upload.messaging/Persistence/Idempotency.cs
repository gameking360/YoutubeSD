using Microsoft.EntityFrameworkCore;
using Upload.Messaging.Kafka;

namespace Upload.Messaging.Persistence;

/// <summary>
/// R1: o messageId entra em processed_messages no mesmo SaveChanges do efeito.
/// Mensagem repetida esbarra na chave primária e não gera efeito duplicado.
/// </summary>
public static class Idempotency
{
    public static Task<bool> AlreadyProcessedAsync(MessagingDbContext context, string messageId, CancellationToken cancellationToken) =>
        context.ProcessedMessages.AsNoTracking().AnyAsync(message => message.MessageId == messageId, cancellationToken);

    public static void Register(MessagingDbContext context, MessageEnvelope message, DateTime now) =>
        context.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = message.MessageId,
            Type = message.Type,
            ProcessedAt = now
        });
}
