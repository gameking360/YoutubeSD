using Microsoft.Extensions.Configuration;

namespace Upload.Messaging.Kafka;

public sealed class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroup { get; set; } = "";

    /// <summary>Tentativas para uma mensagem inválida antes de ir para a DLQ (F4).</summary>
    public int MaxAttempts { get; set; } = 3;

    public KafkaTopics Topics { get; set; } = new();

    public static KafkaSettings Resolve(IConfiguration configuration, string defaultGroup)
    {
        var settings = configuration.GetSection("Kafka").Get<KafkaSettings>() ?? new KafkaSettings();

        var connectionString = configuration.GetConnectionString("kafka");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            settings.BootstrapServers = Normalize(connectionString);
        }

        var fallback = configuration["KAFKA_BOOTSTRAP_SERVERS"];
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            settings.BootstrapServers = Normalize(fallback);
        }

        if (string.IsNullOrWhiteSpace(settings.ConsumerGroup))
        {
            settings.ConsumerGroup = defaultGroup;
        }

        settings.MaxAttempts = Math.Max(1, settings.MaxAttempts);
        return settings;
    }

    private static string Normalize(string value) =>
        string.Join(",", value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(server => server.Contains("://", StringComparison.Ordinal)
                ? server[(server.IndexOf("://", StringComparison.Ordinal) + 3)..]
                : server));
}

/// <summary>Nomes dos tópicos; todos podem ser trocados por configuração (Kafka:Topics:*).</summary>
public sealed class KafkaTopics
{
    public string QuotaReserveRequested { get; set; } = "quota.reserve-requested";
    public string QuotaReserved { get; set; } = "quota.reserved";
    public string QuotaRejected { get; set; } = "quota.rejected";
    public string QuotaReleaseRequested { get; set; } = "quota.release-requested";
    public string VideoUploaded { get; set; } = "video.uploaded";
    public string VideoProcessingStarted { get; set; } = "video.processing-started";
    public string VideoProcessed { get; set; } = "video.processed";
    public string VideoProcessingFailed { get; set; } = "video.processing-failed";
    public string VideoPublished { get; set; } = "video.published";
    public string VideoRemoved { get; set; } = "video.removed";
}
