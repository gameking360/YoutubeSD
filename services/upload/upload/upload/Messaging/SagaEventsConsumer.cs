using Confluent.Kafka;
using Upload.Messaging.Contracts;
using Upload.Messaging.Kafka;
using Upload.Saga;

namespace Upload.Messaging;

/// <summary>
/// Consumidor da saga (grupo "upload"): respostas do Identity e do media-processor.
/// Idempotente por messageId; mensagem inválida vai para a DLQ.
/// </summary>
public sealed class SagaEventsConsumer(
    KafkaSettings kafka,
    IProducer<string, string> producer,
    VideoSagaService saga,
    ILogger<SagaEventsConsumer> logger) : KafkaConsumerLoop(kafka, producer, logger)
{
    private readonly Dictionary<string, string> _subscriptions = new()
    {
        [kafka.Topics.QuotaReserved] = EventTypes.QuotaReserved,
        [kafka.Topics.QuotaRejected] = EventTypes.QuotaRejected,
        [kafka.Topics.VideoProcessingStarted] = EventTypes.VideoProcessingStarted,
        [kafka.Topics.VideoProcessed] = EventTypes.VideoProcessed,
        [kafka.Topics.VideoProcessingFailed] = EventTypes.VideoProcessingFailed
    };

    protected override IReadOnlyDictionary<string, string> Subscriptions => _subscriptions;

    protected override Task ProcessAsync(MessageEnvelope message, string topic, CancellationToken cancellationToken)
    {
        var videoId = message.RequireString("videoId");

        SagaEvent sagaEvent = message.Type switch
        {
            EventTypes.QuotaReserved => new QuotaReservedEvent(),
            EventTypes.QuotaRejected => new QuotaRejectedEvent(message.RequireString("reason")),
            EventTypes.VideoProcessingStarted => new ProcessingStartedEvent(
                message.RequireInt("attempt", minimum: 1),
                message.OptionalString("instanceId") ?? "?"),
            EventTypes.VideoProcessed => new ProcessedEvent(Validate(message.PayloadAs<VideoProcessedPayload>())),
            EventTypes.VideoProcessingFailed => new ProcessingFailedEvent(
                message.RequireString("reason"),
                message.OptionalString("detail")),
            _ => throw new InvalidMessageException($"Tipo não suportado: {message.Type}")
        };

        return saga.HandleMessageAsync(message, videoId, sagaEvent, cancellationToken);
    }

    /// <summary>O VideoPublished repassa estes campos ao Download; nada pode faltar.</summary>
    private static VideoProcessedPayload Validate(VideoProcessedPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.VideoId) ||
            string.IsNullOrWhiteSpace(payload.HlsBucket) ||
            string.IsNullOrWhiteSpace(payload.MasterKey))
        {
            throw new InvalidMessageException("payload.videoId, hlsBucket e masterKey são obrigatórios.");
        }

        if (payload.DurationSec < 1)
        {
            throw new InvalidMessageException("payload.durationSec precisa ser maior ou igual a 1.");
        }

        if (payload.Renditions is null || payload.Renditions.Count == 0 ||
            payload.Renditions.Any(r => r is null || string.IsNullOrWhiteSpace(r.Name) || r.Width < 1 || r.Height < 1 || r.Bandwidth < 1))
        {
            throw new InvalidMessageException("payload.renditions precisa ter ao menos uma resolução válida.");
        }

        return payload with { InstanceId = payload.InstanceId ?? "?" };
    }
}
