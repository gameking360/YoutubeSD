using Confluent.Kafka;
using MediaProcessor.Configuration;
using Upload.Messaging.Contracts;
using Upload.Messaging.Kafka;

namespace MediaProcessor.Processing;

/// <summary>
/// Consome video.uploaded no grupo "media-processor". Rodar 2 ou mais réplicas divide as
/// partições entre elas (R4/F8). Processor:Concurrency abre mais consumidores por instância
/// (escala vertical). O job longo mantém o consumidor vivo no grupo (ver KafkaConsumerLoop).
/// </summary>
public sealed class VideoUploadedConsumer(
    KafkaSettings kafka,
    IProducer<string, string> producer,
    ProcessingService processing,
    ProcessorOptions options,
    ILogger<VideoUploadedConsumer> logger) : KafkaConsumerLoop(kafka, producer, logger)
{
    private readonly Dictionary<string, string> _subscriptions = new()
    {
        [kafka.Topics.VideoUploaded] = EventTypes.VideoUploaded
    };

    protected override IReadOnlyDictionary<string, string> Subscriptions => _subscriptions;

    protected override int Workers => options.Concurrency;

    protected override Task ProcessAsync(MessageEnvelope message, string topic, CancellationToken cancellationToken) =>
        processing.ProcessAsync(message, Validate(message.PayloadAs<VideoUploadedPayload>()), cancellationToken);

    private static VideoUploadedPayload Validate(VideoUploadedPayload job)
    {
        if (string.IsNullOrWhiteSpace(job.VideoId) ||
            string.IsNullOrWhiteSpace(job.RawBucket) ||
            string.IsNullOrWhiteSpace(job.RawKey) ||
            string.IsNullOrWhiteSpace(job.HlsBucket) ||
            string.IsNullOrWhiteSpace(job.HlsPrefix))
        {
            throw new InvalidMessageException("payload.videoId, rawBucket, rawKey, hlsBucket e hlsPrefix são obrigatórios.");
        }

        if (job.QueueDeadline == default)
        {
            throw new InvalidMessageException("payload.queueDeadline é obrigatório.");
        }

        return job.HlsPrefix.EndsWith('/') ? job : job with { HlsPrefix = job.HlsPrefix + "/" };
    }
}
