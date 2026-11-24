namespace Upload.Messaging.Persistence;

/// <summary>Linha com concurrency token: o UPDATE só vale se ninguém mudou a linha antes.</summary>
public interface IVersioned
{
    int Version { get; set; }
}

/// <summary>Idempotência dos consumidores (R1). Mesmo formato da tabela do Download.</summary>
public sealed class ProcessedMessage
{
    public string MessageId { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime ProcessedAt { get; set; }
}

/// <summary>Mensagem gravada na mesma transação do efeito e publicada depois pelo OutboxPublisher.</summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }
    public string MessageId { get; set; } = "";
    public string Topic { get; set; } = "";
    public string MessageKey { get; set; } = "";
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Lease em tabela: elege uma réplica para uma tarefa (ex.: publicar o outbox).</summary>
public sealed class Lease : IVersioned
{
    public string Name { get; set; } = "";
    public string? Owner { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Version { get; set; }
}
